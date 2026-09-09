using Android.Content;

namespace Agnosia.Android.Storage;

public sealed class LocalStorageManager
{
    private const string PreferencesName = "agnosia.preferences";

    private readonly ISharedPreferences _preferences;

    public LocalStorageManager(Context context)
    {
        _preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)
                       ?? throw new InvalidOperationException("Failed to create shared preferences storage.");
    }

    public void Remove(string key)
    {
        using var editor = _preferences.Edit();
        editor?.Remove(key)?.Apply();
    }

    public void RemoveDurably(string key)
    {
        using var editor = _preferences.Edit();
        if (editor?.Remove(key)?.Commit() != true)
            throw new IOException("Failed to durably remove stored value.");
    }

    public void SetValues(IReadOnlyDictionary<string, bool> booleans, IReadOnlyDictionary<string, string> strings)
    {
        using var editor = _preferences.Edit()
            ?? throw new InvalidOperationException("Failed to edit preferences.");
        foreach (var (key, value) in booleans) editor.PutBoolean(key, value);
        foreach (var (key, value) in strings) editor.PutString(key, value);
        if (!editor.Commit()) throw new IOException("Failed to persist settings.");
    }

    public void SetStringDurably(string key, string value)
    {
        using var editor = _preferences.Edit();
        if (editor?.PutString(key, value)?.Commit() != true)
            throw new IOException("Failed to persist pending setting.");
    }

    public bool GetBoolean(string key, bool fallback = false)
    {
        return _preferences.GetBoolean(key, fallback);
    }

    public void SetBoolean(string key, bool value)
    {
        using var editor = _preferences.Edit();
        editor?.PutBoolean(key, value)?.Apply();
    }

    public void SetInt(string key, int value)
    {
        using var editor = _preferences.Edit();
        editor?.PutInt(key, value)?.Apply();
    }

    public long GetLong(string key, long fallback = 0)
    {
        return _preferences.GetLong(key, fallback);
    }

    public void SetLong(string key, long value)
    {
        using var editor = _preferences.Edit();
        editor?.PutLong(key, value)?.Apply();
    }

    public string? GetString(string key)
    {
        return _preferences.GetString(key, null);
    }

    public void SetString(string key, string? value)
    {
        using var editor = _preferences.Edit();
        editor?.PutString(key, value)?.Apply();
    }
}
