namespace SimpleClimb;

/// <summary>
/// A half-finished calculator: the arithmetic that an agent is asked to complete.
/// The unimplemented members throw so their tests fail until the climb fixes them.
/// </summary>
public sealed class Calculator
{
    public int Add(int a, int b) => a + b;

    public int Subtract(int a, int b) => a - b;

    public int Multiply(int a, int b) => throw new NotImplementedException();

    public int Divide(int a, int b) => throw new NotImplementedException();

    public bool IsEven(int value) => throw new NotImplementedException();
}
