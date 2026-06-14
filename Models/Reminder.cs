using System;

namespace Kuromi.Models;

public class Reminder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTime? DueAt { get; set; }
    public bool Done { get; set; }
    public bool Notified { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
