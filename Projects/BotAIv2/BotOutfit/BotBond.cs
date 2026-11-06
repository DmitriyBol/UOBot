using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

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
