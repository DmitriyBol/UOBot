using System;

namespace Server.BotAI.V2;

public interface IBotWantCarrier
{

    bool Carries(BotWant want);

    bool Stale { get; }

    Type Goods { get; }
}
