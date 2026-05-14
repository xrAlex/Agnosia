namespace Agnosia.Models;

public readonly record struct OperationResult(bool Succeeded, string Message)
{
    public static OperationResult Success(string message)
    {
        return new OperationResult(true, message);
    }

    public static OperationResult Failure(string message)
    {
        return new OperationResult(false, message);
    }
}