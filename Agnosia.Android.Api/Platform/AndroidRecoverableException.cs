using Android.Content;
using Java.Lang;
using Exception = System.Exception;

namespace Agnosia.Android.Api.Platform;

public static class AndroidRecoverableException
{
    public static bool IsMatch(Exception exception)
    {
        return exception is ActivityNotFoundException
            or InvalidOperationException
            or IllegalArgumentException
            or IllegalStateException
            or SecurityException;
    }

    public static string ToUserMessage(Exception exception)
    {
        return exception switch
        {
            ActivityNotFoundException => "Android не смог найти экран для выполнения действия.",
            IllegalArgumentException => "Android отклонил параметры системного действия.",
            SecurityException => "Android запретил выполнение действия из-за политики безопасности.",
            InvalidOperationException => "Рабочий профиль или системный сервис сейчас недоступен.",
            _ => "Android не смог выполнить системное действие."
        };
    }
}