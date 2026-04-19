using System.Collections.Generic;
using System.Linq;

namespace CarryOn.Client.Models
{
    public class TransformGroup
    {
        public string GroupName { get; set; }

        public string ExtendsGroup { get; set; }

        public IList<TransformGroupSettings> Base { get; set; }
        public IList<TransformGroupSettings> Overrides { get; set; }
        public IList<TransformGroupSettings> Appends { get; set; }

        public void AddBaseSettings(TransformGroupSettings settings)
        {
            Base ??= new List<TransformGroupSettings>();
            Base.Add(settings);
        }

        public void AddOverrideSettings(TransformGroupSettings settings)
        {
            Overrides ??= new List<TransformGroupSettings>();
            Overrides.Add(settings);
        }

        public void AddAppendSettings(TransformGroupSettings settings)
        {
            Appends ??= new List<TransformGroupSettings>();
            Appends.Add(settings);
        }
    
        public TransformGroup Clone()
        {
            var clone = new TransformGroup
            {
                GroupName = GroupName,
                ExtendsGroup = ExtendsGroup
            };

            if (Base != null) clone.Base = [.. Base.Select(s => s.Clone())];
            if (Overrides != null) clone.Overrides = [.. Overrides.Select(s => s.Clone())];
            if (Appends != null) clone.Appends = [.. Appends.Select(s => s.Clone())];

            return clone;
        }
    }        
}