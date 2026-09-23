using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

/// <summary>
/// What one bot was given, and therefore what death may not take from it.
///
/// <para>
/// <b>Owned by the bot, not by a table somewhere.</b> This is a plain object that the bot holds a
/// reference to, and that is a deliberate break with the first version, where a bot's state lived in
/// thirty-two dictionaries keyed by its serial, spread across as many files. Every one of those needed
/// a <c>Reset</c>, every one leaked when the population was torn down, and answering "what does this
/// bot own" meant reading thirty-two files. A bot that is deleted takes this with it.
/// </para>
///
/// <para>
/// <b>Not serialized, on purpose.</b> The population is rebuilt from configuration on every world
/// load — bots that come back from a save are purged, because the engine's entity serializer has no
/// per-entity opt-out — so a bond only ever has to survive as long as the process. If v2 ever keeps
/// bots across a restart, this is one of the things that has to start being written down.
/// </para>
/// </summary>
public sealed class BotBond
{
    public HashSet<Serial> Items { get; } = [];

    public List<Type> Issued { get; } = [];

    public Dictionary<Type, int> Ammunition { get; } = [];

    public BotWeaponOption? Weapon { get; set; }

    public void Save(IGenericWriter writer)
    {
        writer.Write(Weapon.HasValue);

        if (Weapon.HasValue)
        {
            var weapon = Weapon.Value;

            writer.Write(weapon.Weapon);
            writer.Write((int)weapon.Skill);
            writer.Write(weapon.Target);
            writer.Write(weapon.Ammunition);
            writer.Write(weapon.AmmunitionCount);
        }

        writer.Write(Items.Count);

        foreach (var serial in Items)
        {
            writer.Write(serial);
        }

        writer.Write(Issued.Count);

        for (var i = 0; i < Issued.Count; i++)
        {
            writer.Write(Issued[i]);
        }

        writer.Write(Ammunition.Count);

        foreach (var (type, many) in Ammunition)
        {
            writer.Write(type);
            writer.Write(many);
        }
    }

    public void Load(IGenericReader reader)
    {
        if (reader.ReadBool())
        {
            var weapon = reader.ReadType();
            var skill = (SkillName)reader.ReadInt();
            var target = reader.ReadDouble();
            var ammunition = reader.ReadType();
            var many = reader.ReadInt();

            if (weapon != null)
            {
                Weapon = new BotWeaponOption(weapon, skill, target, ammunition, many);
            }
        }

        var count = reader.ReadInt();

        for (var i = 0; i < count; i++)
        {
            Items.Add(reader.ReadSerial());
        }

        count = reader.ReadInt();

        for (var i = 0; i < count; i++)
        {
            if (reader.ReadType() is { } type)
            {
                Issued.Add(type);
            }
        }

        count = reader.ReadInt();

        for (var i = 0; i < count; i++)
        {
            var type = reader.ReadType();
            var many = reader.ReadInt();

            if (type != null)
            {
                Ammunition[type] = many;
            }
        }
    }
}
