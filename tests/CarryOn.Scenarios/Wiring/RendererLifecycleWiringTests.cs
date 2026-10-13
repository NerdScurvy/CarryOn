using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace CarryOn.Scenarios.Wiring
{
    /// <summary>
    /// Plain xUnit wiring pins for the client renderer's teardown and its runtime toggles.
    ///
    /// <para>The Atlas host boots a headless dedicated server, so <c>Mod.ClientApi</c> is
    /// never assigned and the render path executes in no row (README, "What the suite does
    /// not cover"). These rows pin the *shape* of the missing disposal and invalidation
    /// calls only; the visual outcome stays a manual real-client gate. Do not read a green
    /// row here as "the renderer is covered".</para>
    ///
    /// <para>Both rows read the staged <c>CarryOn.dll</c> with
    /// <c>PEReader</c>/<c>MetadataReader</c> and load no assembly - loading it poisons the
    /// Atlas host's per-install preflight and breaks the version sweep. See
    /// <c>CarryabilitySyncWiringTests</c>.</para>
    /// </summary>
    [Trait("area", "Client/Renderer")]
    [Trait("summary", "Plain xUnit wiring pins: each row reads the staged CarryOn.dll metadata and walks the call graph to assert a renderer teardown or runtime-toggle invalidation is wired, with no server boot and no client render path executed.")]
    public class RendererLifecycleWiringTests
    {
        private const string RendererTypeName = "CarryOn.Client.Logic.CarryRenderer.EntityCarryRenderer";
        private const string DisposeMethodName = "Dispose";
        private const string SetAttachedMethodName = "SetRenderAttachedBlocks";
        private const string LabelRendererDispose = "CarryOn.Client.Logic.CarryRenderer.CarryLabelRenderer.Dispose";
        private const string CacheManagerInvalidateAll = "CarryOn.Client.Logic.CarryRenderer.CarryRenderCacheManager.InvalidateAll";

        /// <summary>
        /// Authored red-first. The renderer owns a label renderer (it disposes the texture
        /// and mesh it holds) but never reached its disposal, so the label textures and the
        /// label quad mesh outlived the renderer. The row accepts any path - a direct call, a
        /// field, or a small helper - because the pin is about reachability, not the exact
        /// call site.
        /// </summary>
        [Trait("summary", "EntityCarryRenderer.Dispose must reach CarryLabelRenderer.Dispose so its textures and quad mesh are released.")]
        [Trait("pins", "The label renderer that EntityCarryRenderer constructs is disposed with the renderer.")]
        [Trait("role", "contract")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head with CarryLabelRenderer.Dispose unreachable from EntityCarryRenderer.Dispose by any call path.")]
        [Trait("caveat", "Wiring only - proves reachability in the call graph, not that the disposal runs, and the renderer is never constructed in this headless suite.")]
        [Fact]
        public void EntityCarryRenderer_Dispose_ReleasesTheLabelRenderer()
        {
            using var peReader = new PEReader(File.OpenRead(StagedModProbe.ModAssemblyPath()));
            var reader = peReader.GetMetadataReader();

            var reaches = StagedModProbe.ReachesMember(
                reader, peReader, RendererTypeName, DisposeMethodName, LabelRendererDispose);
            Assert.True(
                reaches,
                $"{RendererTypeName}.{DisposeMethodName}() must reach {LabelRendererDispose}(). "
                + "The renderer constructs the label renderer, which owns label textures and a "
                + "quad mesh; without a disposal path those GPU resources outlive the renderer "
                + "and leak on every client teardown. The disposal may be direct, via a field, "
                + "or via a small helper - this pin only requires it to be reachable.");
        }

        /// <summary>
        /// Authored red-first. Toggling the attached-blocks option flipped flags on the render
        /// info builder and the dispatcher but left the caches built under the previous
        /// setting in place, so the toggle took effect only for slots the caches later
        /// rebuilt. The row asserts the toggle reaches the cache flush.
        /// </summary>
        [Trait("summary", "EntityCarryRenderer.SetRenderAttachedBlocks must reach the render-cache invalidation so the toggle is not served from stale cached render infos.")]
        [Trait("pins", "The attached-blocks runtime toggle invalidates the render caches that were built under the previous setting.")]
        [Trait("role", "contract")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head with the cache invalidation unreachable from SetRenderAttachedBlocks - it only flipped the two flags.")]
        [Trait("caveat", "Wiring only - proves the invalidation is wired, not that the visual toggle takes effect; the real-client visual outcome is a manual gate.")]
        [Fact]
        public void SetRenderAttachedBlocks_InvalidatesRenderCaches()
        {
            using var peReader = new PEReader(File.OpenRead(StagedModProbe.ModAssemblyPath()));
            var reader = peReader.GetMetadataReader();

            var reaches = StagedModProbe.ReachesMember(
                reader, peReader, RendererTypeName, SetAttachedMethodName, CacheManagerInvalidateAll);
            Assert.True(
                reaches,
                $"{RendererTypeName}.{SetAttachedMethodName}() must reach "
                + $"{CacheManagerInvalidateAll}(). The method flips the attached-blocks flag "
                + "on the render info builder and the dispatcher, but the per-slot sidecar and "
                + "plan caches still hold render infos built under the previous setting, so the "
                + "toggle is inert until something else flushes them. Invalidate the render "
                + "caches when the setting changes.");
        }
    }
}
