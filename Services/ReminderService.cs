using System.Collections.Generic;
using Kuromi.Models;

namespace Kuromi.Services;

public class ReminderService
{
    private readonly ConfigService _config;

    public ReminderService(ConfigService config) => _config = config;

    public List<Reminder> Load()
        => _config.LoadJson(_config.RemindersPath, () => new List<Reminder>());

    public void Save(List<Reminder> reminders)
        => _config.SaveJson(_config.RemindersPath, reminders);
}
