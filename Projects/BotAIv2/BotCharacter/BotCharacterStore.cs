using System;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotCharacterStore : GenericPersistence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCharacterStore));

    private const int Shape = 2;

    private static BotCharacterStore _store;

    public static void Configure() => _store ??= new BotCharacterStore();

    public BotCharacterStore() : base("BotCharacter", 20)
    {
    }

    public override void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(Shape);
        writer.WriteEncodedInt(BotLives.Count);

        foreach (var life in BotLives.All)
        {
            writer.Write(life.Name);
            writer.WriteEncodedInt((int)life.Traits);
            writer.Write(life.Kills);
            writer.WriteEncodedInt(life.Deaths);
            writer.WriteEncodedInt(life.SavedTimes);
            writer.WriteEncodedInt(life.RaisedTimes);
            writer.WriteEncodedInt(life.Robbed);
            writer.WriteEncodedInt(life.WarKills);
            writer.WriteEncodedInt(life.DuelsWon);
            writer.WriteEncodedInt(life.DuelsLost);
            writer.WriteEncodedInt(life.BestSale);
            writer.Write(life.Veteran);
            writer.Write(life.VeteranAt);

            writer.WriteEncodedInt(life.Tales.Count);

            for (var i = 0; i < life.Tales.Count; i++)
            {
                var tale = life.Tales[i];

                writer.Write(tale.Kind);
                writer.Write(tale.Other);
                writer.Write(tale.Text);
                writer.Write(tale.Weight);
                writer.WriteEncodedInt(tale.Number);
                writer.Write(tale.At);
                writer.Write(tale.ToldAt);
            }

            writer.WriteEncodedInt(life.Ties.Count);

            for (var i = 0; i < life.Ties.Count; i++)
            {
                var tie = life.Ties[i];

                writer.Write(tie.Name);
                writer.Write(tie.Regard);
                writer.WriteEncodedInt(tie.Saved);
                writer.WriteEncodedInt(tie.Wronged);
                writer.WriteEncodedInt(tie.Beside);
            }

            writer.WriteEncodedInt(life.HeardTales.Count);

            for (var i = 0; i < life.HeardTales.Count; i++)
            {
                var heard = life.HeardTales[i];

                writer.Write(heard.Teller);
                writer.Write(heard.Kind);
                writer.Write(heard.About);
                writer.Write(heard.Text);
                writer.Write(heard.Place);
                writer.Write(heard.At);
                writer.WriteEncodedInt(heard.Hops);
            }
        }
    }

    public override void Deserialize(IGenericReader reader)
    {
        var shape = reader.ReadEncodedInt();

        if (shape is < 1 or > Shape)
        {
            logger.Warning("The saved characters are shape {Found} and this build reads {Wanted}; every nature is drawn again from the names, and no story or tie is kept", shape, Shape);

            return;
        }

        var count = reader.ReadEncodedInt();
        var ties = 0;
        var tales = 0;
        var veterans = 0;

        for (var n = 0; n < count; n++)
        {
            var life = new BotLife(reader.ReadString())
            {
                Traits = (BotTrait)reader.ReadEncodedInt(),
                Drawn = true,
                Kills = reader.ReadDouble(),
                Deaths = reader.ReadEncodedInt(),
                SavedTimes = reader.ReadEncodedInt(),
                RaisedTimes = reader.ReadEncodedInt(),
                Robbed = reader.ReadEncodedInt(),
                WarKills = reader.ReadEncodedInt(),
                DuelsWon = reader.ReadEncodedInt(),
                DuelsLost = reader.ReadEncodedInt(),
                BestSale = reader.ReadEncodedInt(),
                Veteran = reader.ReadBool(),
                VeteranAt = reader.ReadDateTime()
            };

            var taleCount = reader.ReadEncodedInt();

            for (var i = 0; i < taleCount; i++)
            {
                life.Tales.Add(
                    new BotTale
                    {
                        Kind = reader.ReadString(),
                        Other = reader.ReadString(),
                        Text = reader.ReadString(),
                        Weight = reader.ReadDouble(),
                        Number = reader.ReadEncodedInt(),
                        At = reader.ReadDateTime(),
                        ToldAt = shape >= 2 ? reader.ReadDateTime() : default
                    }
                );
            }

            var tieCount = reader.ReadEncodedInt();

            for (var i = 0; i < tieCount; i++)
            {
                life.Ties.Add(
                    new BotTie
                    {
                        Name = reader.ReadString(),
                        Regard = reader.ReadDouble(),
                        Saved = reader.ReadEncodedInt(),
                        Wronged = reader.ReadEncodedInt(),
                        Beside = reader.ReadEncodedInt()
                    }
                );
            }

            if (shape >= 2)
            {
                var heardCount = reader.ReadEncodedInt();

                for (var i = 0; i < heardCount; i++)
                {
                    life.HeardTales.Add(
                        new BotHeardTale
                        {
                            Teller = reader.ReadString(),
                            Kind = reader.ReadString(),
                            About = reader.ReadString(),
                            Text = reader.ReadString(),
                            Place = reader.ReadString(),
                            At = reader.ReadDateTime(),
                            Hops = reader.ReadEncodedInt()
                        }
                    );
                }
            }

            if (string.IsNullOrEmpty(life.Name))
            {
                continue;
            }

            life.Rewound();
            BotLives.Put(life);

            ties += life.Ties.Count;
            tales += life.Tales.Count;
            veterans += life.Veteran ? 1 : 0;
        }

        logger.Information(
            "Characters read back: {Count} bots with their natures, {Tales} tales, {Ties} ties and {Veterans} veterans",
            count,
            tales,
            ties,
            veterans
        );
    }
}
