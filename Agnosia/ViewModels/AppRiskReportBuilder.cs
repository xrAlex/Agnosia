using Agnosia.Models;
using System.Globalization;
using static Agnosia.ViewModels.AppRiskReportTextCatalog;

namespace Agnosia.ViewModels;

public static class AppRiskReportBuilder
{
    private enum Access { Requested, Foreground, Available }

    public static AppRiskReportViewModel Build(AppSnapshot snapshot, bool isModuleEnabled,
        bool isRefreshing, bool refreshFailed, TimeZoneInfo? zone = null)
    {
        if (!isModuleEnabled) return Empty(string.Empty);
        if (!snapshot.PermissionRiskAvailable)
            return Empty(isRefreshing ? "Проверяем доступы приложения…"
                : refreshFailed ? "Не удалось выполнить проверку" : "Оценка пока недоступна");

        var findings = snapshot.PermissionRiskFindings ?? [];
        var context = new ReportContext(findings);
        var sections = context.BuildSections();
        var level = snapshot.PermissionRiskLevel;
        var summary = findings.Count == 0
            ? level == AppPermissionRiskLevel.Safe
                ? "Проверка не выявила опасных возможностей."
                : "Для этой оценки пока нет подробного описания."
            : context.BuildSummary();

        return new(level, level switch
        {
            AppPermissionRiskLevel.Critical => "Критический риск",
            AppPermissionRiskLevel.Dangerous => "Повышенный риск",
            _ => findings.Count > 0 ? "Низкий риск" : string.Empty
        }, snapshot.PermissionRiskEvaluatedAtUtc is { } timestamp
            ? "Проверено " + TimeZoneInfo.ConvertTime(timestamp, zone ?? TimeZoneInfo.Local).ToString("dd.MM.yy HH:mm", CultureInfo.InvariantCulture)
            : string.Empty,
            summary, sections,
            isRefreshing ? "Обновляем оценку…" : refreshFailed
                ? "Не удалось обновить оценку. Показаны результаты предыдущей проверки." : string.Empty,
            snapshot.IsInternetBlocked ? "Для приложения включена блокировка интернета" : string.Empty)
        {
            SourceFindings = findings.ToArray(),
            MeaningIds = context.MeaningIds
        };
    }

    private static AppRiskReportViewModel Empty(string status) =>
        new(AppPermissionRiskLevel.Safe, string.Empty, string.Empty, string.Empty, [], status, string.Empty);

    private sealed class ReportContext
    {
        private readonly IReadOnlyList<AppPermissionRiskFinding> _findings;
        private readonly Dictionary<string, AppPermissionRiskEvidence[]> _evidence;
        private readonly Dictionary<string, List<AppPermissionRiskFinding>> _sources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Access> _access = new(StringComparer.Ordinal);
        private readonly List<(Topic Topic, string[] Ids, AppPermissionRiskLevel Level)> _topics;

        public IReadOnlyList<string> MeaningIds => _sources.Keys.Order(StringComparer.Ordinal).ToArray();

        public ReportContext(IReadOnlyList<AppPermissionRiskFinding> findings)
        {
            _findings = findings;
            _evidence = findings.SelectMany(f => f.Evidence).GroupBy(e => e.SignalId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Distinct().ToArray(), StringComparer.Ordinal);
            foreach (var finding in findings)
            {
                if (!Rules.TryGetValue(finding.RuleId, out var rule)) continue;
                foreach (var id in GetMeanings(finding, rule))
                {
                    if (!_sources.TryGetValue(id, out var sources)) _sources[id] = sources = [];
                    sources.Add(finding);
                }
            }
            foreach (var id in _sources.Keys) _access[id] = Evaluate(Meanings[id]);
            RequireUnderlyingAccess("location-background", "location-fine", "location-coarse");
            RequireHealthDataAccess();
            RequireUnderlyingAccess("body-background", "body-sensors");
            var visible = Meanings.Keys.Where(_sources.ContainsKey)
                .Where(id => id != "location-coarse" || !Available("location-fine") || !Available(id))
                .Where(id => id != "health-read" || !_access.TryGetValue("health-history", out var history) || history != _access[id]);
            _topics = visible.GroupBy(id => Meanings[id].Topic)
                .Select(g => (Topic: g.Key, Ids: g.ToArray(), Level: g.SelectMany(id => _sources[id]).Max(f => f.RuleLevel)))
                .OrderByDescending(g => g.Level).ThenBy(g => g.Topic).ToList();
        }

        private static IEnumerable<string> GetMeanings(AppPermissionRiskFinding finding, Rule rule)
        {
            foreach (var id in rule.Meanings) yield return id;
            if (!rule.Meanings.Contains("health-read")) yield break;
            // The engine's health-read prefix includes these conditions even offline.
            foreach (var (signal, id) in new[]
            {
                ("health.READ_HEALTH_DATA_IN_BACKGROUND", "health-background"),
                ("health.READ_HEALTH_DATA_HISTORY", "health-history")
            })
                if (!rule.Meanings.Contains(id) && finding.Evidence.Any(e => e.SignalId == Signal(signal))) yield return id;
        }

        public IReadOnlyList<AppRiskReportSectionViewModel> BuildSections()
        {
            var sections = new List<AppRiskReportSectionViewModel>();
            foreach (var (topic, ids, _) in _topics)
            {
                var sentences = ids.Chunk(2).Select(RenderParagraph).ToList();
                AddCombinationConsequences(topic, sentences);
                AddTransfers(ids, sentences);
                sections.Add(new(Title(topic, ids), sentences));
            }
            var conditions = BuildConditions();
            if (conditions.Count > 0) sections.Add(new("Дополнительные возможности", conditions));
            if (_findings.Any(f => !Rules.ContainsKey(f.RuleId)))
                sections.Add(new("Другие возможности", ["Найден дополнительный риск, для которого пока нет подробного описания."]));
            return sections;
        }

        public string BuildSummary()
        {
            if (_topics.Count < 2) return string.Empty;
            return string.Join(" ", _topics.Take(2).Select(t => Render(
                t.Ids.FirstOrDefault(id => id is "location-background" or "health-background" or "body-background") ?? t.Ids[0], false)));
        }

        private string Render(string id, bool consequence = true)
        {
            var meaning = Meanings[id];
            var access = _access[id];
            var text = access switch
            {
                Access.Requested => "Приложение запрашивает возможность " + Action(id) + ".",
                Access.Foreground => "Приложение может " + Action(id) + " во время использования.",
                _ => meaning.ConfirmedText ?? "Приложение может " + Action(id) + "."
            };
            if (consequence && access != Access.Requested && meaning.Consequence.Length > 0)
                text += " " + meaning.Consequence;
            return text;
        }

        private string RenderParagraph(string[] ids)
        {
            if (ids.Length == 1 || _access[ids[0]] != _access[ids[1]] || ids.Any(id => Meanings[id].ConfirmedText is not null))
                return string.Join(" ", ids.Select(id => Render(id)));
            if (ids.Any(id => Action(id).Contains(',') || Action(id).Contains(':')))
                return string.Join(" ", ids.Select(id => Render(id)));
            var state = _access[ids[0]];
            var text = (state == Access.Requested ? "Приложение запрашивает возможность " : "Приложение может ")
                + Join(ids.Select(Action)) + (state == Access.Foreground ? " во время использования." : ".");
            if (state != Access.Requested)
                foreach (var consequence in ids.Select(id => Meanings[id].Consequence).Where(s => s.Length > 0).Distinct())
                    text += " " + consequence;
            return text;
        }

        private string Action(string id)
        {
            if (id == "health-background" && _access[id] == Access.Available
                && GetHealthDataStates().Any(state => state != Access.Available))
                return "читать часть записей о здоровье, когда приложение не открыто на экране";
            if (id == "selected-media" && Available("files-all"))
                return "читать выбранные вами фотографии и видео";
            if (id == "media-location")
            {
                if (_sources.ContainsKey("photos") && !_sources.ContainsKey("videos") && !_sources.ContainsKey("files-all") && !_sources.ContainsKey("files-legacy"))
                    return "узнавать место съёмки из доступных ему фотографий";
                if (_sources.ContainsKey("videos") && !_sources.ContainsKey("photos") && !_sources.ContainsKey("files-all") && !_sources.ContainsKey("files-legacy"))
                    return "узнавать место съёмки из доступных ему видео";
            }
            return Meanings[id].Action;
        }

        private void RequireUnderlyingAccess(string condition, params string[] underlying)
        {
            if (_access.ContainsKey(condition) && !underlying.Any(id => _access.TryGetValue(id, out var state) && state == Access.Available))
                _access[condition] = Access.Requested;
        }

        private void RequireHealthDataAccess()
        {
            if (_access.ContainsKey("health-background") && !GetHealthDataStates().Contains(Access.Available))
                _access["health-background"] = Access.Requested;
            // History changes the date range; it does not require background access.
            if (_access.TryGetValue("health-history", out var history))
                _access["health-history"] = (Access)Math.Min((int)history,
                    (int)_access.GetValueOrDefault("health-read", Access.Requested));
        }

        private IEnumerable<Access> GetHealthDataStates() =>
            _sources.TryGetValue("health-read", out var sources)
                ? sources.SelectMany(f => f.Evidence).Select(e => e.SignalId)
                    .Where(s => s.StartsWith("android.permission.health.READ_", StringComparison.Ordinal)
                        && s is not "android.permission.health.READ_HEALTH_DATA_IN_BACKGROUND"
                            and not "android.permission.health.READ_HEALTH_DATA_HISTORY")
                    .Distinct(StringComparer.Ordinal).Select(s => EvaluateSignal(s))
                : [];

        private Access Evaluate(Meaning meaning)
        {
            var required = meaning.Signals.Select(Signal).ToArray();
            if (meaning.Prefix)
            {
                // Health background/history are conditions, not readable health data types.
                required = _sources[meaning.Id].SelectMany(f => f.Evidence).Select(e => e.SignalId)
                    .Where(s => required.Any(prefix => s.StartsWith(prefix, StringComparison.Ordinal)))
                    .Where(s => s is not "android.permission.health.READ_HEALTH_DATA_IN_BACKGROUND"
                        and not "android.permission.health.READ_HEALTH_DATA_HISTORY")
                    .Distinct(StringComparer.Ordinal).ToArray();
            }
            if (required.Length == 0) return Access.Requested;
            var states = required.Select(s => EvaluateSignal(s, meaning.DeclaredAvailable)).ToArray();
            if (states.Contains(Access.Requested)) return Access.Requested;
            return states.Contains(Access.Foreground) ? Access.Foreground : Access.Available;
        }

        private Access EvaluateSignal(string signal, bool declaredAvailable = false)
        {
            if (!_evidence.TryGetValue(signal, out var entries) || entries.Length == 0) return Access.Requested;
            if (entries.Any(e => e.State is AppPermissionRiskEvidenceState.Denied or AppPermissionRiskEvidenceState.Disabled
                or AppPermissionRiskEvidenceState.Unknown || e.State == AppPermissionRiskEvidenceState.Declared && !declaredAvailable))
                return Access.Requested;
            return entries.Any(e => e.State == AppPermissionRiskEvidenceState.ForegroundOnly) ? Access.Foreground : Access.Available;
        }

        private bool Available(string id) => _access.TryGetValue(id, out var state) && state != Access.Requested;

        private void AddCombinationConsequences(Topic topic, List<string> sentences)
        {
            if (topic == Topic.Contacts && Available("contacts-read") && Available("accounts") && Available("phone-number"))
                sentences.Add("Это позволяет связывать ваши контакты с аккаунтами и номером телефона.");
            if (topic != Topic.Screen) return;
            if (Available("overlay") && (Available("accessibility") || Available("notifications")))
                sentences.Add("Приложение может показывать окна, которые можно принять за окна другого приложения.");
            if (Available("accessibility") && Available("notifications"))
                sentences.Add("Приложение может связывать прочитанные уведомления с действиями в других приложениях.");
        }

        private void AddTransfers(string[] ids, List<string> sentences)
        {
            var transferIds = ids.Contains("health-history") && _sources.ContainsKey("health-read") ? ids.Append("health-read") : ids;
            var objects = transferIds.Where(Available).Where(id => Meanings[id].TransferObject is not null)
                .Where(id => _sources[id].Any(f => Rules[f.RuleId].Internet &&
                    f.Evidence.Any(e => e.SignalId == Signal("INTERNET"))) &&
                    EvaluateSignal(Signal("INTERNET"), true) == Access.Available)
                .Select(id => Meanings[id].TransferObject!).Distinct(StringComparer.Ordinal).ToArray();
            if (objects.Length > 0)
                sentences.Add("Приложение также может передавать через интернет " + Join(objects) + ".");
            if (!ids.Contains("screen-capture") || !Available("screen-capture")) return;
            var channels = _sources["screen-capture"].Where(f => Rules[f.RuleId].ScreenChannels)
                .SelectMany(f => f.Evidence).Select(e => e.SignalId).ToHashSet(StringComparer.Ordinal);
            bool Has(string name, bool declared = false) => channels.Contains(Signal(name)) &&
                EvaluateSignal(Signal(name), declared) == Access.Available;
            var routes = new List<string>();
            if (Has("INTERNET", true)) routes.Add("через интернет");
            if (Has("ACCESS_LOCAL_NETWORK")) routes.Add("другим устройствам в той же сети");
            if (Has("BLUETOOTH_CONNECT")) routes.Add("подключённым устройствам Bluetooth");
            if (Has("NFC", true)) routes.Add("устройству, поднесённому вплотную к телефону");
            if (Has("SEND_SMS")) routes.Add("в СМС, что может приводить к расходам на связь");
            if (routes.Count > 0)
                sentences.Add("Данные с экрана могут передаваться " + Join(routes) + ".");
            if (Has("MANAGE_EXTERNAL_STORAGE") || Has("WRITE_EXTERNAL_STORAGE"))
                sentences.Add("Данные с экрана могут сохраняться в общих файлах на телефоне.");
        }

        private IReadOnlyList<string> BuildConditions()
        {
            var result = new List<string>();
            AddCondition("RECEIVE_BOOT_COMPLETED", true,
                "Приложение может запускаться после включения телефона.",
                "Приложение запрашивает запуск после включения телефона.");
            AddCondition("REQUEST_IGNORE_BATTERY_OPTIMIZATIONS", false,
                "Для приложения ослаблены ограничения, связанные с экономией батареи.",
                "Приложение запрашивает исключение из некоторых ограничений, связанных с экономией батареи.");

            foreach (var (id, service, action) in new[]
            {
                ("microphone", "microphone", "продолжать запись звука"),
                ("camera", "camera", "продолжать съёмку камерой"),
                ("location-fine", "location", "продолжать определять местоположение"),
                ("location-coarse", "location", "продолжать определять местоположение")
            })
            {
                if (!_sources.TryGetValue(id, out var sources)) continue;
                if (sources.Any(f => f.Evidence.Any(e => e.SignalId == "android.foregroundServiceType." + service &&
                    e.State is not AppPermissionRiskEvidenceState.Denied and not AppPermissionRiskEvidenceState.Disabled)))
                {
                    // A declared service type is a request, not proof of a running service.
                    var text = "Приложение запрашивает возможность " + action + ", когда вы переходите в другие приложения.";
                    if (!result.Contains(text, StringComparer.Ordinal)) result.Add(text);
                }
            }
            if (_sources.TryGetValue("usage", out var usage) && usage.Any(f => f.Evidence.Any(e =>
                e.SignalId == Signal("FOREGROUND_SERVICE"))) && EvaluateSignal(Signal("FOREGROUND_SERVICE"), true) == Access.Available)
                result.Add("Приложение запрашивает возможность продолжать работу, когда вы переходите в другие приложения.");
            return result;

            void AddCondition(string permission, bool declaredAvailable, string confirmed, string requested)
            {
                var signal = Signal(permission);
                if (!_findings.Any(f => Rules.ContainsKey(f.RuleId) && f.Evidence.Any(e => e.SignalId == signal))) return;
                var text = EvaluateSignal(signal, declaredAvailable) == Access.Available ? confirmed : requested;
                result.Add(text);
            }
        }

        private static string Title(Topic topic, string[] ids) => topic switch
        {
            Topic.Location => "Местоположение", Topic.Calls => "Звонки", Topic.Messages => "Сообщения",
            Topic.Contacts => ids.Contains("accounts") ? "Контакты и аккаунты" : "Контакты",
            Topic.Calendar => "Календарь",
            Topic.Recording => ids.Contains("microphone") && ids.Contains("camera") ? "Микрофон и камера"
                : ids.Contains("microphone") ? "Микрофон" : "Камера",
            Topic.Screen => "Экран и уведомления", Topic.Files => FileTitle(ids),
            Topic.Health => "Здоровье и активность", Topic.Usage => "Данные о приложениях",
            Topic.Ads => "Реклама", Topic.Nearby => "Устройства поблизости", _ => "Управление телефоном"
        };

        private static string FileTitle(string[] ids)
        {
            if (ids.Any(id => id is "files-all" or "files-legacy" or "files-write")) return "Файлы";
            if (ids.Contains("audio"))
            {
                var photos = ids.Contains("photos") || ids.Contains("selected-media");
                var videos = ids.Contains("videos") || ids.Contains("selected-media");
                return (photos, videos) switch
                {
                    (true, true) => "Фотографии, видео и аудио", (true, false) => "Фотографии и аудио",
                    (false, true) => "Видео и аудио", _ => "Аудиофайлы"
                };
            }
            if (ids.Contains("selected-media") || ids.Contains("photos") && ids.Contains("videos")) return "Фотографии и видео";
            return ids.Contains("photos") ? "Фотографии" : "Видеозаписи";
        }
    }

    private static string Signal(string name) => name.StartsWith("android.", StringComparison.Ordinal) ||
        name.StartsWith("com.", StringComparison.Ordinal) ? name : "android.permission." + name;

    private static string Join(IEnumerable<string> items)
    {
        var list = items.ToArray();
        return list.Length < 2 ? list.FirstOrDefault() ?? string.Empty
            : string.Join(", ", list[..^1]) + " и " + list[^1];
    }
}
