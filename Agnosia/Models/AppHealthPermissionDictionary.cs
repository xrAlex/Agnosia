namespace Agnosia.Models;

/// <summary>
/// Health Connect permissions used by ordinary apps from Android 9 onward.
/// Individual data types depend on the Health Connect version; names match
/// android.health.connect.HealthPermissions through API 36.
/// </summary>
internal static class AppHealthPermissionDictionary
{
    private const string Prefix = "android.permission.health.";

    public static void AddTo(Dictionary<string, AppPermissionText> entries)
    {
        void Add(string name, string label, string description) =>
            entries.Add(Prefix + name, new AppPermissionText(label, description));

        foreach (var line in PairedData.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException($"Некорректная запись разрешения Health Connect: {line}");

            var (name, label, subject) = (parts[0], parts[1], parts[2]);
            entries.Add(Prefix + "READ_" + name,
                new AppPermissionText($"{label} — чтение",
                    $"Позволяет читать сведения о {subject} из хранилища данных о здоровье."));
            entries.Add(Prefix + "WRITE_" + name,
                new AppPermissionText($"{label} — запись",
                    $"Позволяет записывать сведения о {subject} в хранилище данных о здоровье."));
        }

        Add("READ_EXERCISE_ROUTES", "Маршруты тренировок — чтение",
            "Позволяет читать сохранённые маршруты тренировок из хранилища данных о здоровье.");
        Add("READ_HEALTH_DATA_HISTORY", "Давние данные о здоровье",
            "Позволяет читать данные о здоровье старше обычного 30-дневного периода.");
        Add("READ_HEALTH_DATA_IN_BACKGROUND", "Данные о здоровье при закрытом приложении",
            "Позволяет читать ранее разрешённые данные о здоровье, когда приложение не открыто.");
        Add("READ_MEDICAL_DATA_ALLERGIES_INTOLERANCES", "Аллергии и непереносимость — чтение",
            "Позволяет читать медицинские записи об аллергиях и непереносимости.");
        Add("READ_MEDICAL_DATA_CONDITIONS", "Заболевания — чтение",
            "Позволяет читать медицинские записи о заболеваниях.");
        Add("READ_MEDICAL_DATA_LABORATORY_RESULTS", "Результаты анализов — чтение",
            "Позволяет читать медицинские записи с результатами лабораторных анализов.");
        Add("READ_MEDICAL_DATA_MEDICATIONS", "Лекарства — чтение",
            "Позволяет читать медицинские записи о лекарствах.");
        Add("READ_MEDICAL_DATA_PERSONAL_DETAILS", "Личные медицинские данные — чтение",
            "Позволяет читать личные сведения из медицинских записей.");
        Add("READ_MEDICAL_DATA_PRACTITIONER_DETAILS", "Врачи — чтение",
            "Позволяет читать сведения о врачах из медицинских записей.");
        Add("READ_MEDICAL_DATA_PREGNANCY", "Беременность — чтение",
            "Позволяет читать медицинские записи о беременности.");
        Add("READ_MEDICAL_DATA_PROCEDURES", "Медицинские процедуры — чтение",
            "Позволяет читать медицинские записи о процедурах.");
        Add("READ_MEDICAL_DATA_SOCIAL_HISTORY", "Привычки и образ жизни — чтение",
            "Позволяет читать сведения о привычках и образе жизни из медицинских записей.");
        Add("READ_MEDICAL_DATA_VACCINES", "Прививки — чтение",
            "Позволяет читать медицинские записи о прививках.");
        Add("READ_MEDICAL_DATA_VISITS", "Посещения врача — чтение",
            "Позволяет читать записи о посещениях врачей и клиник.");
        Add("READ_MEDICAL_DATA_VITAL_SIGNS", "Показатели здоровья — чтение",
            "Позволяет читать медицинские записи о жизненных показателях, например пульсе и давлении.");
        Add("WRITE_EXERCISE_ROUTE", "Маршрут тренировки — запись",
            "Позволяет сохранять маршрут тренировки в хранилище данных о здоровье.");
        Add("WRITE_MEDICAL_DATA", "Медицинские записи — добавление",
            "Позволяет добавлять медицинские записи в хранилище данных о здоровье.");
    }

    // Name suffix | short title | subject after «сведения о».
    private const string PairedData = """
        ACTIVE_CALORIES_BURNED|Активно потраченные калории|калориях, потраченных при движении
        ACTIVITY_INTENSITY|Интенсивность активности|интенсивности физической активности
        BASAL_BODY_TEMPERATURE|Базальная температура|базальной температуре тела
        BASAL_METABOLIC_RATE|Основной обмен веществ|расходе энергии в состоянии покоя
        BLOOD_GLUCOSE|Сахар в крови|уровне сахара в крови
        BLOOD_PRESSURE|Артериальное давление|артериальном давлении
        BODY_FAT|Доля жира в теле|доле жира в теле
        BODY_TEMPERATURE|Температура тела|температуре тела
        BODY_WATER_MASS|Вода в организме|количестве воды в организме
        BONE_MASS|Костная масса|массе костей
        CERVICAL_MUCUS|Выделения шейки матки|наблюдениях за выделениями шейки матки
        DISTANCE|Пройденное расстояние|пройденном расстоянии
        ELEVATION_GAINED|Набор высоты|наборе высоты при движении
        EXERCISE|Тренировки|тренировках
        FLOORS_CLIMBED|Поднятые этажи|количестве этажей, пройденных вверх
        HEART_RATE|Пульс|частоте сердцебиения
        HEART_RATE_VARIABILITY|Изменчивость пульса|колебаниях времени между ударами сердца
        HEIGHT|Рост|росте
        HYDRATION|Выпитая вода|количестве выпитой воды
        INTERMENSTRUAL_BLEEDING|Кровотечение между менструациями|кровотечениях между менструациями
        LEAN_BODY_MASS|Масса тела без жира|массе тела без жировой ткани
        MENSTRUATION|Менструация|менструации
        MINDFULNESS|Практики осознанности|практиках осознанности, например медитации
        NUTRITION|Питание|питании
        OVULATION_TEST|Тест на овуляцию|результатах тестов на овуляцию
        OXYGEN_SATURATION|Кислород в крови|насыщении крови кислородом
        PLANNED_EXERCISE|План тренировок|запланированных тренировках
        POWER|Мощность тренировки|мощности во время тренировки
        RESPIRATORY_RATE|Частота дыхания|частоте дыхания
        RESTING_HEART_RATE|Пульс в покое|пульсе в состоянии покоя
        SEXUAL_ACTIVITY|Сексуальная активность|сексуальной активности
        SKIN_TEMPERATURE|Температура кожи|температуре кожи
        SLEEP|Сон|сне
        SPEED|Скорость движения|скорости движения
        STEPS|Шаги|количестве шагов
        TOTAL_CALORIES_BURNED|Все потраченные калории|общем количестве потраченных калорий
        VO2_MAX|Потребление кислорода при нагрузке|максимальном потреблении кислорода при нагрузке
        WEIGHT|Вес|весе
        WHEELCHAIR_PUSHES|Толчки инвалидной коляски|количестве толчков инвалидной коляски
        """;
}
