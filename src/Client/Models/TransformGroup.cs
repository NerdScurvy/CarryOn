using System.Collections.Generic;
using System.Linq;

namespace CarryOn.Client.Models
{
    public record TransformGroup(
        string GroupName,
        string ExtendsGroup,
        IReadOnlyList<TransformGroupSettings> Base,
        IReadOnlyList<TransformGroupSettings> Overrides,
        IReadOnlyList<TransformGroupSettings> Appends
    )
    {
        // Deep clone method
        public TransformGroup DeepClone() => this with
        {
            Base = Base?.Select(s => s.DeepClone()).ToList(),
            Overrides = Overrides?.Select(s => s.DeepClone()).ToList(),
            Appends = Appends?.Select(s => s.DeepClone()).ToList()
        };
    }          
}