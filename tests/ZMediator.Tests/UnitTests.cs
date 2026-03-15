namespace ZMediator.Tests;

public class UnitTests
{
    [Fact]
    public void Unit_IsReadonlyRecordStruct()
    {
        var unit = new Unit();
        Assert.Equal(default(Unit), unit);
        Assert.Equal(unit, new Unit());
    }

    [Fact]
    public void Unit_Value_ReturnsSingleton()
    {
        var a = Unit.Value;
        var b = Unit.Value;
        Assert.Equal(a, b);
    }
}
