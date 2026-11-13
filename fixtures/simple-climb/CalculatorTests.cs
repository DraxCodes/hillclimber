namespace SimpleClimb;

public class CalculatorTests
{
    private readonly Calculator _calculator = new();

    [Fact]
    public void Add_SumsTwoNumbers() => Assert.Equal(5, _calculator.Add(2, 3));

    [Fact]
    public void Add_IsCommutative() => Assert.Equal(_calculator.Add(4, 9), _calculator.Add(9, 4));

    [Fact]
    public void Subtract_SubtractsTwoNumbers() => Assert.Equal(2, _calculator.Subtract(7, 5));

    [Fact]
    public void Multiply_MultipliesTwoNumbers() => Assert.Equal(12, _calculator.Multiply(3, 4));

    [Fact]
    public void Divide_DividesTwoNumbers() => Assert.Equal(4, _calculator.Divide(12, 3));

    [Fact]
    public void IsEven_DetectsEvenNumbers()
    {
        Assert.True(_calculator.IsEven(10));
        Assert.False(_calculator.IsEven(7));
    }
}
