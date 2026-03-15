namespace ZMediator.Tests;

public class PipelineBehaviorAttributeTests
{
    [Fact]
    public void PipelineBehaviorAttribute_DefaultOrder_IsZero()
    {
        var attr = new PipelineBehaviorAttribute();
        Assert.Equal(0, attr.Order);
        Assert.Null(attr.AppliesTo);
    }

    [Fact]
    public void PipelineBehaviorAttribute_WithOrder_SetsOrder()
    {
        var attr = new PipelineBehaviorAttribute(5);
        Assert.Equal(5, attr.Order);
    }

    [Fact]
    public void PipelineBehaviorAttribute_WithAppliesTo_SetsType()
    {
        var attr = new PipelineBehaviorAttribute { AppliesTo = typeof(string) };
        Assert.Equal(typeof(string), attr.AppliesTo);
    }

    [Fact]
    public void IPipelineBehavior_IsMarkerInterface()
    {
        var members = typeof(IPipelineBehavior).GetMembers(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.DeclaredOnly);
        Assert.Empty(members);
    }
}
