using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Models;
using CarryOn.Client.Models;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace CarryOn.Client.Logic.CarryRenderer
{
	public class EntityCarryRenderer : IRenderer
	{
		private static readonly Dictionary<CarrySlot, Dictionary<string, SlotRenderSettings>> RenderSettings = CreateRenderSettings();
		private static Dictionary<CarrySlot, Dictionary<string, SlotRenderSettings>> CreateRenderSettings() => new() {
			{ CarrySlot.Hands    , new Dictionary<string, SlotRenderSettings> {
					{ "hands", new SlotRenderSettings("carryon:FrontCarry", -0.3F, -0.6F, -0.5F) } } },

			{ CarrySlot.Back     , new Dictionary<string, SlotRenderSettings> {
					{ "backpack-none", new SlotRenderSettings("Back", -0.3F, -0.6F, -0.5F) },
					{ "backpack-small", new SlotRenderSettings("Back", -0.2F, -0.6F, -0.5F) },
					{ "backpack-large", new SlotRenderSettings("Back", -0.025F, -0.6F, -0.5F) }
				}
			}
		};

		private record QueuedDraw(
			CarriedRenderInfo Info,
			float[] Matrix,
			bool IsRoot,
			RenderPhaseMask Phases,
			float AlphaTestOpaque,
			float AlphaTestBlend
		);

		
		private record SlotRenderSettings
		{
			public string AttachmentPoint { get; }
			public Vec3f Offset { get; }
			public SlotRenderSettings(string attachmentPoint, float xOffset, float yOffset, float zOffset)
			{ AttachmentPoint = attachmentPoint; Offset = new Vec3f(xOffset, yOffset, zOffset); }
		}

		private const float PlantTintBrightnessBoost = 1.12f;
		private readonly Vec4f plantTintScratch = new(1f, 1f, 1f, 1f);

		private Vec4f GetRenderTint(CarriedRenderInfo info)
		{
			var sourceTint = info.TintColor ?? CarryRenderHelpers.DefaultTint;
			if (!info.EnableVertexWarp)
			{
				return sourceTint;
			}

			plantTintScratch.X = Math.Min(1f, sourceTint.X * PlantTintBrightnessBoost);
			plantTintScratch.Y = Math.Min(1f, sourceTint.Y * PlantTintBrightnessBoost);
			plantTintScratch.Z = Math.Min(1f, sourceTint.Z * PlantTintBrightnessBoost);
			plantTintScratch.W = sourceTint.W;
			return plantTintScratch;
		}

		private CarrySystem carrySystem { get; }
		private ICoreClientAPI api { get; }
		private readonly HashSet<string> activeCarryAnimations = new(StringComparer.OrdinalIgnoreCase);

		private long renderTick = 0;
		private readonly CarryRenderCache cache = new();
		private readonly CarryTransformPlanBuilder planBuilder;
		private readonly CarryRenderInfoBuilder infoBuilder;
		private readonly CarriedLabelRenderer labelRenderer;

		// Matrix pool for per-frame reuse to reduce GC pressure
		private readonly Stack<float[]> matrixPool = new();

		private float[] RentMatrix() => this.matrixPool.Count > 0 ? this.matrixPool.Pop() : new float[16];


		public EntityCarryRenderer(ICoreClientAPI api, CarrySystem carrySystem)
		{
            ArgumentNullException.ThrowIfNull(api);
            ArgumentNullException.ThrowIfNull(carrySystem);

			this.carrySystem = carrySystem;
			this.api = api;
			this.api.Event.RegisterRenderer(this, EnumRenderStage.Opaque);
			this.api.Event.RegisterRenderer(this, EnumRenderStage.AfterOIT);
			this.api.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar);
			this.api.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear);
			planBuilder = new CarryTransformPlanBuilder(api, carrySystem, cache);
			infoBuilder = new CarryRenderInfoBuilder(api);
			labelRenderer = new CarriedLabelRenderer(api);
		}

		// We don't have any unmanaged resources to dispose.
		public void Dispose()
		{
			this.api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
			this.api.Event.UnregisterRenderer(this, EnumRenderStage.AfterOIT);
			this.api.Event.UnregisterRenderer(this, EnumRenderStage.ShadowFar);
			this.api.Event.UnregisterRenderer(this, EnumRenderStage.ShadowNear);
			cache.InvalidateAll();
			labelRenderer?.Dispose();
		}

		public void InvalidateRenderCaches()
		{
			cache.InvalidateAll();
		}

		private CarriedRenderInfo[] GetRenderInfoCached(EntityAgent entity, CarriedBlock carried, string transformsGroup)
		{
			var slotStateKey = CarryRenderHelpers.BuildSlotStateKey(entity, carried.Slot);
			var plan = planBuilder.GetOrBuild(carried, transformsGroup);

			// Include BE rotation + slot content signature so cached render infos invalidate
			// when displaycase item orientation/contents change.
			var containerSlots = TransformGroupResolverHelper.GetContainerSlots(carried);
			var renderVariantSignature = CarryRenderHelpers.BuildRenderInfoVariantSignature(
				carried,
				containerSlots,
				plan.EffectiveSettings,
				this.api.World);

			var frameKey = CarryRenderHelpers.BuildFrameCacheKey(entity, carried, plan.Signature, renderVariantSignature);
			cache.InvalidateSlotState(slotStateKey, frameKey);

			if (cache.FrameRenderInfos.TryGetValue(frameKey, out var frameCached))
			{
				return CarryRenderHelpers.CloneCarriedRenderInfos(frameCached);
			}

			var now = DateTime.UtcNow;

			var renderInfoKey = string.Concat(plan.Signature, "|ri|", renderVariantSignature);
			cache.SlotStates[slotStateKey] = new SlotCacheState
			{
				FrameKey = frameKey,
				PlanSignature = plan.Signature,
				RenderInfoKey = renderInfoKey
			};

			if (cache.RenderInfos.TryGetValue(renderInfoKey, out var cachedRenderInfos))
			{
				cachedRenderInfos.LastUsedAtUtc = now;
				var clonedFromPersistent = CarryRenderHelpers.CloneCarriedRenderInfos(cachedRenderInfos.RenderInfos);
				cache.FrameRenderInfos[frameKey] = CarryRenderHelpers.CloneCarriedRenderInfos(clonedFromPersistent);
				return clonedFromPersistent;
			}

			var built = infoBuilder.BuildFromPlan(carried, plan, containerSlots);
			cache.RenderInfos[renderInfoKey] = new CachedRenderInfos
			{
				Signature = renderInfoKey,
				RenderInfos = built,
				LastUsedAtUtc = now
			};

			cache.FrameRenderInfos[frameKey] = built;
			return built;
		}

		// IRenderer implementation

		public double RenderOrder => 1.0;
		public int RenderRange => 99;

		private void SyncCarryAnimations(EntityPlayer player)
		{
			if (player == null) return;

			var isSneaking = player.Controls?.Sneak ?? false;
			var isSitting = CarryRenderHelpers.IsSitting(player);
			var desiredAnimations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var carried in player.GetCarried())
			{
				var animationCode = carried.GetCarryableBehavior()?.Slots?[carried.Slot]?.Animation;
				animationCode = CarryRenderHelpers.ResolveCarryAnimationCode(animationCode, isSneaking, isSitting);
				if (!string.IsNullOrEmpty(animationCode))
				{
					desiredAnimations.Add(animationCode);
				}
			}

			// Start any new desired carryon:hold* animations
			foreach (var animationCode in desiredAnimations.Except(this.activeCarryAnimations))
				player.StartAnimation(animationCode);


			// Stop any carryon:hold* animations that are no longer desired
			foreach (var animationCode in this.activeCarryAnimations.Except(desiredAnimations)
																	.Where(code => code.StartsWith("carryon:hold", StringComparison.OrdinalIgnoreCase))
																	.ToList())
				player.StopAnimation(animationCode);
			

			// Update the active set
			this.activeCarryAnimations.Clear();
			this.activeCarryAnimations.UnionWith(desiredAnimations);
		}

		public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
		{
			// Clear matrix pool at the start of each frame
			matrixPool.Clear();
			if (stage != EnumRenderStage.Opaque
				&& stage != EnumRenderStage.AfterOIT
				&& stage != EnumRenderStage.ShadowFar
				&& stage != EnumRenderStage.ShadowNear)
			{
				return;
			}

			if (stage == EnumRenderStage.Opaque)
			{
				cache.ClearFrameCache();
				cache.PruneTransformPlans();
				cache.PruneRenderInfos();
			}

			foreach (var player in this.api.World.AllPlayers)
			{
				// Player entity may be null in some circumstances..?
				if (player.Entity == null) continue;

				var isShadowPass = stage == EnumRenderStage.ShadowFar || stage == EnumRenderStage.ShadowNear;

				SyncCarryAnimations(player.Entity);

				// Don't render remote entities that haven't been rendered for this stage.
				if (player != this.api.World.Player
					&& (isShadowPass ? !player.Entity.IsShadowRendered : !player.Entity.IsRendered))
				{
					continue;
				}

				RenderAllCarried(player.Entity, deltaTime, stage, isShadowPass);
			}
			this.renderTick++;
		}

		/// <summary> Renders all carried blocks of the specified entity. </summary>
		private void RenderAllCarried(EntityAgent entity, float deltaTime, EnumRenderStage stage, bool isShadowPass)
		{
			var config = this.carrySystem.ClientConfig.Config;
			if (config.CarriedLightEnabled)
			{
				// Reset light so carried block light can be added later
				entity.LightHsv = new ThreeBytes(0);
			}

			var allCarried = entity.GetCarried().ToList();
			if (allCarried.Count == 0) return; // Entity is not carrying anything.

			var player = this.api.World.Player;
			var isLocalPlayer = entity == player.Entity;
			var isFirstPerson = isLocalPlayer && (player.CameraMode == EnumCameraMode.FirstPerson);
			var isImmersiveFirstPerson = player.ImmersiveFpMode;

			var renderer = (EntityShapeRenderer)entity.Properties.Client.Renderer;
			var animator = entity.AnimManager.Animator;

			// Rendered not ready?
			if (renderer == null) return;

			if (config.CarriedLightEnabled)
			{
				// Merge carried block light into entity light so that it applies to the entity and all carried blocks, without needing to modify each CarriedRenderInfo.
				var lightHsv = entity.LightHsv;
				foreach (var carried in allCarried)
				{
					switch (carried.ItemStack?.Collectible)
					{
						case Block block:
							lightHsv = ColorUtil.MergeLightHSV(block.LightHsv, lightHsv);
							break;
						case Item item:
							lightHsv = ColorUtil.MergeLightHSV(item.LightHsv, lightHsv);
							break;
					}
				}
				entity.LightHsv = lightHsv;
			}

			foreach (var carried in allCarried)
			{
				RenderCarried(entity, carried, deltaTime,
							  isFirstPerson, isImmersiveFirstPerson,
							  stage, isShadowPass, renderer, animator);
			}
		}

		/// <summary> 
		/// Renders the specified queued draws for either the opaque or translucent phase (depending on the translucentPhase parameter). 
		/// </summary>
		private void RenderQueuedPhase(
			List<QueuedDraw> draws,
			bool translucentPhase,
			IStandardShaderProgram prog,
			float[] viewMat,
			EntityShapeRenderer renderer)
		{
			var rapi = this.api.Render;

			// Global phase state
			if (translucentPhase)
			{
				rapi.GLDepthMask(false);
				rapi.GlToggleBlend(true, 0);
			}
			else
			{
				rapi.GLDepthMask(true);
				rapi.GlToggleBlend(false, 0);
			}

			try
			{
				foreach (var d in draws)
				{
					if (!CarryRenderHelpers.ShouldDrawInPhase(d.Phases, translucentPhase)) continue;

					var info = d.Info;
					bool disabledCull = false;

					try
					{
						if (!info.RenderInfo.CullFaces)
						{
							rapi.GlDisableCullFace();
							disabledCull = true;
						}

						prog.Tex2D = info.RenderInfo.TextureId;
						prog.ViewMatrix = viewMat;
						prog.ModelMatrix = d.Matrix;
						prog.DontWarpVertices = info.EnableVertexWarp ? 2 : 1;
						prog.RgbaTint = GetRenderTint(info);

						// Phase-specific alpha threshold
						prog.AlphaTest = translucentPhase ? d.AlphaTestBlend : d.AlphaTestOpaque;

						prog.NormalShaded = info.NormalShaded.HasValue ? (info.NormalShaded.Value ? 1 : 0) : 1;
						prog.RgbaGlowIn = info.RgbGlowIntensity ?? new Vec4f(0f, 0f, 0f, 0f);
						
						rapi.RenderMultiTextureMesh(info.RenderInfo.ModelRef, "tex");
					}
					finally
					{
						if (disabledCull) rapi.GlEnableCullFace();
					}
				}
			}
			finally
			{
				// Restore defaults
				rapi.GLDepthMask(true);
				rapi.GlToggleBlend(false, 0);
			}
		}

		/// <summary>
		/// Renders a single carried block for the given entity and render pass.
		/// </summary>
		private void RenderCarried(EntityAgent entity, CarriedBlock carried, float deltaTime,
								   bool isFirstPerson, bool isImmersiveFirstPerson, EnumRenderStage stage, bool isShadowPass,
								   EntityShapeRenderer renderer, IAnimator animator)
		{
			var rapi = this.api.Render;
			var inHands = carried.Slot == CarrySlot.Hands;
			if (!inHands && isFirstPerson && !isShadowPass) return; // Only Hands slot is rendered in first person.

			var deferHandsOpaqueUntilAfterOit = inHands && isFirstPerson && !isImmersiveFirstPerson;
			if (!isShadowPass)
			{
				if (stage == EnumRenderStage.Opaque && deferHandsOpaqueUntilAfterOit) return;
			}

			var viewMat = Array.ConvertAll(rapi.CameraMatrixOrigin, i => (float)i);

			string transformGroupName = entity.ResolveCarryTransformGroupBase(carrySystem, carried.Slot);

			var renderSettings = RenderSettings?[carried.Slot]?[transformGroupName];

			// If render settings are not found for the current slot and transform group, don't render.
			if (renderSettings == null) return;

			var carriedRenderInfo = GetRenderInfoCached(entity, carried, transformGroupName);

			if (carriedRenderInfo == null || carriedRenderInfo.Length == 0) return; // No render info for this carried block.

			float[] modelMat;
			if (inHands && isFirstPerson && !isImmersiveFirstPerson && !isShadowPass)
			{
				modelMat = GetFirstPersonHandsMatrix(entity, viewMat, deltaTime);
				// Move carried block down slightly so not in players face as much in first person/non-immersive view.
				Mat4f.Translate(modelMat, modelMat, 0.0F, -0.05F, 0.0F);
			}
			else
			{
				if (animator == null) return;
				var attachPointAndPose = animator.GetAttachmentPointPose(renderSettings.AttachmentPoint);
				if (attachPointAndPose == null) return; // Couldn't find attachment point.
				modelMat = CarryRenderHelpers.GetAttachmentPointMatrix(renderer, attachPointAndPose);
			}

			float[] initialMatrix = RentMatrix();
			float[] matrix;

			var initial = carriedRenderInfo.FirstOrDefault();
			initial.SkipTransform = true;
			Array.Copy(modelMat, initialMatrix, 16);
			CarryRenderHelpers.ApplyTransformInPlace(initial.RenderInfo.Transform, initialMatrix, renderSettings.Offset);

			var renderRootFirst = carried.GetCarryableBehavior()?.RenderRootFirst ?? false;
			if (carriedRenderInfo.Length > 1 && !renderRootFirst)
			{
				carriedRenderInfo = carriedRenderInfo.Skip(1).Append(initial).ToArray();
			}

			// Zero offset for secondary transforms - they're already positioned relative to the root
			var zeroOffset = new Vec3f(0, 0, 0);

			if (isShadowPass)
			{
				var prog = rapi.CurrentActiveShader;
				foreach (var info in carriedRenderInfo)
				{
					if (!info.RenderEnabled) continue;
					if (info.SkipTransform)
					{
						matrix = initialMatrix;
					}
					else
					{
						matrix = RentMatrix();
						Array.Copy(initialMatrix, matrix, 16);
						CarryRenderHelpers.ApplyTransformInPlace(info.RenderInfo.Transform, matrix, zeroOffset);
						if (info.SecondaryTransform != null)
						{
							CarryRenderHelpers.ApplyTransformInPlace(info.SecondaryTransform, matrix, zeroOffset);
						}
					}

					var shadowMatrix = RentMatrix();
					Array.Copy(matrix, shadowMatrix, 16);
					Mat4f.Mul(shadowMatrix, rapi.CurrentShadowProjectionMatrix, shadowMatrix);

					bool disabledCull = false;
					if (!info.RenderInfo.CullFaces)
					{
						rapi.GlDisableCullFace();
						disabledCull = true;
					}

					try
					{
						prog.BindTexture2D("tex2d", info.RenderInfo.TextureId, 0);
						prog.UniformMatrix("mvpMatrix", shadowMatrix);
						prog.Uniform("origin", renderer.OriginPos);

						rapi.RenderMultiTextureMesh(info.RenderInfo.ModelRef, "tex2d");
					}
					finally
					{
						if (disabledCull)
						{
							rapi.GlEnableCullFace();
						}
					}
					matrixPool.Push(matrix);
					matrixPool.Push(shadowMatrix);
				}
			}
			else
			{
				var renderOpaquePhase = stage == EnumRenderStage.Opaque || (stage == EnumRenderStage.AfterOIT && deferHandsOpaqueUntilAfterOit);
				var renderTranslucentPhase = stage == EnumRenderStage.AfterOIT;

				if (!renderOpaquePhase && !renderTranslucentPhase)
				{
					return;
				}

				var prog = rapi.PreparedStandardShader((int)entity.Pos.X, (int)entity.Pos.Y, (int)entity.Pos.Z);

				// 1) Build draw queue
				var draws = new List<QueuedDraw>(carriedRenderInfo.Length);
				foreach (var info in carriedRenderInfo)
				{
					if (!info.RenderEnabled) continue;

					var drawMatrix = RentMatrix();
					Array.Copy(initialMatrix, drawMatrix, 16);
					if (!info.SkipTransform)
					{
						CarryRenderHelpers.ApplyTransformInPlace(info.RenderInfo.Transform, drawMatrix, zeroOffset);
						if (info.SecondaryTransform != null)
						{
							CarryRenderHelpers.ApplyTransformInPlace(info.SecondaryTransform, drawMatrix, zeroOffset);
						}
					}

					draws.Add(new QueuedDraw
					(
						Info: info,
						Matrix: drawMatrix,
						IsRoot: info.SkipTransform,
						Phases: CarryRenderHelpers.ResolveDefaultPhases(info),
						AlphaTestOpaque: info.AlphaTestOpaque ?? 0.5f,
						AlphaTestBlend: info.AlphaTestBlend ?? 0.15f
					));
				}

				// 2) Add root to either the front or back of the queue, depending on whether root-first rendering is specified. Root-first rendering is important for certain cases where the root transform has alpha and we want to render it in the opaque pass, while rendering secondary transforms in the translucent pass.
				// renderRootFirst=true => root first
				var roots = new List<QueuedDraw>();
				var nonRoots = new List<QueuedDraw>();
				foreach (var d in draws)
				{
					if (d.IsRoot) roots.Add(d);
					else nonRoots.Add(d);
				}
				draws = renderRootFirst
					? [.. roots, .. nonRoots]
					: [.. nonRoots, .. roots];

				if (renderOpaquePhase)
				{
					// Global phase A: opaque for all draws
					RenderQueuedPhase(draws, translucentPhase: false, prog, viewMat, renderer);
				}

				if (renderTranslucentPhase)
				{
					// Global phase B: translucent for all draws
					RenderQueuedPhase(draws, translucentPhase: true, prog, viewMat, renderer);
				}

				if (renderOpaquePhase && initialMatrix != null)
				{
					labelRenderer.TryRender(carried, initialMatrix, viewMat, prog, entity.Pos.AsBlockPos);
				}

				foreach (var d in draws)
				{
					matrixPool.Push(d.Matrix);
				}

				prog.Stop();
			}
		}

		// The most recent tick that the hands were rendered.
		private long lastTickHandsRendered = 0;
		private float moveWobble;
		private float lastYaw;
		private float yawDifference;

		/// <summary> 
		/// Gets the transform matrix for rendering the player's hands in first person, including view wobble based on movement and rotation. 
		/// </summary>
		/// <param name="entity"> The entity whose hands are being rendered. </param>
		/// <param name="viewMat"> The current view matrix. </param>
		/// <param name="deltaTime"> The time elapsed since the last frame. </param>
		/// <returns> The transform matrix for the player's hands. </returns>
		private float[] GetFirstPersonHandsMatrix(EntityAgent entity, float[] viewMat, float deltaTime)
		{
			var modelMat = Mat4f.Invert(Mat4f.Create(), viewMat);

			// If the hands haven't been rendered in the last 10 render ticks, reset wobble and such.
			if (this.renderTick - this.lastTickHandsRendered > 10)
			{
				this.moveWobble = 0;
				this.lastYaw = entity.Pos.Yaw;
				this.yawDifference = 0;
			}
			this.lastTickHandsRendered = this.renderTick;

			if (entity.Controls.TriesToMove)
			{
				var moveSpeed = entity.Controls.MovespeedMultiplier * (float)entity.GetWalkSpeedMultiplier();
				this.moveWobble += moveSpeed * deltaTime * 5.0F;
			}
			else
			{
				var target = (float)(Math.Round(this.moveWobble / Math.PI) * Math.PI);
				var speed = deltaTime * (0.2F + (Math.Abs(target - this.moveWobble) * 4));
				if (Math.Abs(target - this.moveWobble) < speed) this.moveWobble = target;
				else this.moveWobble += Math.Sign(target - this.moveWobble) * speed;
			}
			this.moveWobble %= GameMath.PI * 2;

			var moveWobbleOffsetX = GameMath.Sin(this.moveWobble + GameMath.PI) * 0.03F;
			var moveWobbleOffsetY = GameMath.Sin(this.moveWobble * 2) * 0.02F;

			this.yawDifference += GameMath.AngleRadDistance(this.lastYaw, entity.Pos.Yaw);
			this.yawDifference *= (1 - 0.075F);
			this.lastYaw = entity.Pos.Yaw;

			var yawRotation = -this.yawDifference / 2;
			var pitchRotation = (entity.Pos.Pitch - GameMath.PI) / 4;

			Mat4f.RotateY(modelMat, modelMat, yawRotation);
			Mat4f.Translate(modelMat, modelMat, 0.0F, -0.35F, -0.20F);
			Mat4f.RotateY(modelMat, modelMat, -yawRotation);
			Mat4f.RotateX(modelMat, modelMat, pitchRotation / 2);
			Mat4f.Translate(modelMat, modelMat, 0.0F, 0.0F, -0.20F);
			Mat4f.RotateX(modelMat, modelMat, pitchRotation);
			Mat4f.RotateY(modelMat, modelMat, yawRotation);

			Mat4f.Translate(modelMat, modelMat, moveWobbleOffsetX, moveWobbleOffsetY, 0.0F);
			Mat4f.RotateY(modelMat, modelMat, 90.0F * GameMath.DEG2RAD);

			return modelMat;
		}
	}
}

