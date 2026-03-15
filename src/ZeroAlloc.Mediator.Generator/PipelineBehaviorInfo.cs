#nullable enable
using System;

namespace ZMediator.Generator
{
    internal sealed class PipelineBehaviorInfo : IEquatable<PipelineBehaviorInfo>
    {
        public string BehaviorTypeName { get; }
        public int Order { get; }
        public string? AppliesTo { get; }
        public bool HasValidHandleMethod { get; }

        public PipelineBehaviorInfo(string behaviorTypeName, int order, string? appliesTo, bool hasValidHandleMethod)
        {
            BehaviorTypeName = behaviorTypeName;
            Order = order;
            AppliesTo = appliesTo;
            HasValidHandleMethod = hasValidHandleMethod;
        }

        public bool Equals(PipelineBehaviorInfo? other)
        {
            if (other is null) return false;
            return BehaviorTypeName == other.BehaviorTypeName
                && Order == other.Order
                && AppliesTo == other.AppliesTo
                && HasValidHandleMethod == other.HasValidHandleMethod;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as PipelineBehaviorInfo);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + BehaviorTypeName.GetHashCode();
                hash = hash * 31 + Order.GetHashCode();
                hash = hash * 31 + (AppliesTo?.GetHashCode() ?? 0);
                hash = hash * 31 + HasValidHandleMethod.GetHashCode();
                return hash;
            }
        }
    }
}
