using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Server.Services;

public static class EventInstanceDtoFactory
{
    public static Dtos.EventInstanceDto From(EventInstance inst, RunState s, DataCatalog catalog)
    {
        var def = catalog.Events[inst.EventId];
        var choices = def.Choices.Select(c =>
        {
            string? summary = c.Condition switch
            {
                EventCondition.MinGold cond => $"requires {cond.Amount} gold",
                EventCondition.MinHp cond => $"requires {cond.Amount} HP",
                null => null,
                _ => "requires condition",
            };
            bool met = c.Condition switch
            {
                null => true,
                EventCondition.MinGold cond => s.Gold >= cond.Amount,
                EventCondition.MinHp cond => s.CurrentHp >= cond.Amount,
                _ => false,
            };
            return new Dtos.EventChoiceSnapshotDto(c.Label, summary, met);
        }).ToList();
        return new Dtos.EventInstanceDto(inst.EventId, def.Name, def.Description, choices);
    }
}
