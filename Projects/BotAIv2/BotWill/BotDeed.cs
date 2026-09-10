namespace Server.BotAI.V2;

/// <summary>
/// One undertaking: a piece of work with stages, held until it finishes, fails or is dropped.
///
/// <para>
/// <b>Its stages are its own business.</b> Mining is not "walk to a vein" — it is dig, then smelt, then bank
/// what came of it, and an undertaking that ends at the vein leaves a bot standing underground holding ore
/// nobody can buy. So the undertaking, not the brain, knows the sequence: it is asked what to do now, over
/// and over, and it answers with a place, with "work here", or with an ending. The brain never learns what
/// ore is.
/// </para>
///
/// <para>
/// <b>This is what replaces the first version's one big method.</b> There, choosing was a single function of
/// 1209 lines inside a file of 7985 that referenced 57 other modules, so every change to any behaviour was a
/// change to that file. Here a new kind of work is a new folder with a proposer and a subclass of this, and
/// nothing in <c>BotWill/</c> is touched at all.
/// </para>
///
/// <para>
/// <b>It is also where the bot's state lives while the work is in progress.</b> An undertaking is created per
/// bot by its proposer and held on that bot's <see cref="BotResolve"/> — so a proposer keeps no table keyed
/// by serial, and a deleted bot takes its half-finished business with it.
/// </para>
/// </summary>
public abstract class BotDeed
{
    /// <summary>
    /// Short, stable key for what kind of work this is — <c>mine</c>, <c>smelt</c>, <c>hunt</c>. It is the
    /// key the ledger measures against and the word that appears in the log, so it names a <em>kind</em> of
    /// work and never one instance of it: "mine" learns from thirty trips, "mine-vein-4193" learns from one.
    /// </summary>
    public abstract string Kind { get; }

    /// <summary>Which map the work happens on. An undertaking on another map is not offered.</summary>
    public abstract Map Map { get; }

    /// <summary>
    /// Where the work happens, roughly. Used for two things only — how far away it is, and which patch of
    /// ground the ledger files the outcome under — so the centre of a mine is as good as a particular vein.
    /// </summary>
    public abstract Point3D Where { get; }

    /// <summary>
    /// What the takings are expected to come to, in gold-equivalent per minute, before this bot's own
    /// experience is taken into account. The proposer's claim, and the only number it is asked for.
    ///
    /// <para>
    /// It is a prior, not a promise: <see cref="BotLedger"/> blends it with what actually happened the last
    /// few times this bot did this here, and measurement wins as evidence accumulates. That is the whole
    /// learning mechanism, and it needs no model — a proposer that is systematically optimistic gets
    /// corrected by the shard rather than argued with.
    /// </para>
    /// </summary>
    public abstract double Expects { get; }

    /// <summary>
    /// Which skill this work trains, or null for work that trains nothing.
    ///
    /// <para>
    /// Named rather than inferred, and credited only when the undertaking actually <em>finishes</em>. Both
    /// halves are anti-exploit measures against the metric this project is built on. Reward the raw rate of
    /// skill gain and the best available behaviour is the cheapest repeatable twitch that gains skill — a
    /// training dummy, a spell cast at nothing, two bots sparring in a field for ever. Tying the credit to a
    /// finished piece of work means the gain has to arrive alongside ore, a corpse or a delivery.
    /// </para>
    /// </summary>
    public virtual SkillName? Trains => null;

    /// <summary>
    /// Gold that must be in hand before this can start — reagents, tools, ammunition, a share of a pot.
    ///
    /// <b>This is what need is measured against.</b> Not a comfort threshold: the first version compared
    /// every purse to a flat 250 and so was born with the entire population reading as short of money, and a
    /// signal that is on for everybody always is not a signal. What a bot cannot afford is what it was about
    /// to try to do.
    /// </summary>
    public virtual int Outlay => 0;

    /// <summary>
    /// How much of the takings arrive as money rather than as skill or as goods, from 0 to 1.
    ///
    /// The one place where being short of money changes what a bot picks. Everything here is measured in one
    /// currency so that wants can be compared, but a purse of skill does not buy a pickaxe — so a bot that
    /// needs coin discounts work that pays in anything else, in proportion to how badly it needs it.
    /// </summary>
    public virtual double Coin => 1.0;

    /// <summary>
    /// How long the work itself is expected to take, in minutes, once the bot is there. Used to weigh the
    /// walk against the work: half an hour of digging is worth a five-minute walk and five minutes of digging
    /// is not.
    /// </summary>
    public virtual double Minutes => 5.0;

    /// <summary>
    /// Gold-equivalent of goods this has produced so far and not sold — ingots, cloth, bandages, potions.
    ///
    /// Counted as takings in its own right, so that making something for the population is not automatically
    /// beaten by hawking loot at a shopkeeper. That is not tidiness: bot-to-bot trade moves gold about,
    /// while selling to a shopkeeper <em>creates</em> it, so a brain that only ever counted coin would drive
    /// the whole population into the one faucet the first version already drained by 110,900 in a night.
    /// </summary>
    public virtual int Made => 0;

    /// <summary>
    /// What the bot should do now. Called once per decision while this undertaking is held, and it is the
    /// only method that matters.
    ///
    /// <para>
    /// The undertaking may look at anything it likes about the bot — where it is, what it carries, whether
    /// <see cref="BotJourney.Arrived"/> is true of its own last request — and it advances its own stages. It
    /// must be cheap: it runs on the population's beat, per bot.
    /// </para>
    /// </summary>
    public abstract BotDoing Advance(IBotWilful bot);

    /// <summary>
    /// The walk this undertaking asked for turned out to be impossible, and it is being told so. Return true
    /// to carry on — with somewhere else in mind — or false to be given up.
    ///
    /// <para>
    /// The same shape one level up as movement's own rule that the goal is untouchable and the way bends: a
    /// second forge is a decision only the work knows how to make, so the brain asks instead of guessing.
    /// The default is to give up, because a subsystem that has nothing to say here should not have its bot
    /// looping.
    /// </para>
    /// </summary>
    public virtual bool Bend(IBotWilful bot) => false;

    /// <summary>
    /// Whether this will not keep. Asked of an offer only, and it answers one question: may this jump the
    /// floor that protects whatever the bot has already taken on.
    ///
    /// <para>
    /// <b>Written for the thing standing next to the bot.</b> Almost everything on offer here is a place —
    /// a vein, a forge, a counter — and a place is still there in half a minute, which is exactly why the
    /// dwell exists and why it is right. A creature is not: it walks, it is claimed by somebody else, it
    /// kills the bot's neighbour and moves on. A bot that answers "I will consider it after the next thirty
    /// seconds of digging" is a bot that walks past an ogre, and that is what this is for.
    /// </para>
    ///
    /// <para>
    /// The default is false and should stay false for nearly everything: a rung where everything is pressing
    /// is a rung with no dwell, and no dwell is the first version's bot that changed its mind four times a
    /// second and finished nothing.
    /// </para>
    /// </summary>
    public virtual bool Pressing(IBotWilful bot) => false;

    /// <summary>
    /// Whether this work goes on alongside a squad rather than being displaced by one.
    ///
    /// <para>
    /// <b>Almost nothing may say yes.</b> A squad owns where its members stand — it rebases the bottom of
    /// every member's journey each beat — so a private errand to somewhere else is overwritten within the
    /// second, and that is why joining one sets everything else aside. The exception is work whose whole
    /// subject <em>is</em> the squad: it asks the company for nothing, sends the bot nowhere, and is there
    /// to see the thing through and be paid for it. Without the exception such work would be handed out and
    /// then frozen by the very squad it just called together, and the takings — which arrive as a share of
    /// what the company killed — would be credited to nothing at all.
    /// </para>
    /// </summary>
    /// <summary>
    /// Whether this work is not about money, and must not be refused for failing to earn any.
    ///
    /// <para>
    /// <b>One deed on this shard is paid nothing on purpose, and the arithmetic threw it away for exactly
    /// that.</b> The Baron's rounds pay nothing by design — they are where he is between harrowings — and
    /// while he walks them he spends: on kit, on the stipend, on what a company costs. The ledger measured
    /// that honestly and concluded rounds were worth -40.5 gold a minute, whereupon
    /// <see cref="BotAppraisal"/>'s "expected to pay nothing" veto refused them. With harrowings unavailable
    /// — a fresh population has no grandmasters, so no company can be raised for damned ground — the Baron
    /// was offered one thing, refused it, and stood in Britain for twelve minutes with 600gp in his pack.
    /// </para>
    ///
    /// <para>
    /// The flag rather than a floor on the estimate: a floor would quietly rescue every unprofitable errand
    /// on the shard, including the ones that <i>ought</i> to be refused, and the reason this one is different
    /// is a fact about the work rather than about its price.
    /// </para>
    /// </summary>
    public virtual bool Unpaid => false;

    /// <summary>
    /// Whether this work can be done by a bot that cannot take a single step.
    ///
    /// <para>
    /// <b>Almost nothing can, and the default says so.</b> A bot past its carrying ceiling is refused every
    /// move by the engine, so an errand that begins with a walk is an errand it will hold, fail, and be
    /// offered again — Joss stood on one tile for thirteen minutes taking and dropping thirty-two of them,
    /// and Roderic reached 271 stones of a 218 ceiling on 08.09.2026 while taking peddling at 183 a minute
    /// over the unloading that would have freed him at 74.
    /// </para>
    ///
    /// <para>
    /// The one thing that is true here is putting goods on the market, because a stall holds its wares
    /// wherever the seller stands. So <c>BotUnload</c> answers true and everything else keeps the default.
    /// </para>
    ///
    /// <para>
    /// <b>What is done with the answer changed on 09.09.2026, and the change is the point of the flag.</b>
    /// It was first a veto in <c>BotAppraisal</c>, which left heavy bots with nothing at all to do for
    /// twenty-seven minutes at a stretch; then a factor there, which left them doing exactly what they had
    /// been doing, because a fiftieth under a fifth root is 0.46. It is read in <c>BotWill.Auction</c> as a
    /// <em>rank</em> now: for a bot past its carrying ceiling, everything that answers true is compared only
    /// with the others that do, and everything else is compared beneath them. A price cannot express "this
    /// cannot be started" at any value; an order can, and it keeps the floor as well, since a bot with
    /// nothing standing on offer simply has one rank and the auction it always had.
    /// </para>
    /// </summary>
    public virtual bool Standing => false;

    public virtual bool Alongside => false;

    /// <summary>
    /// Whether this work has already spent something that dropping it will not get back.
    ///
    /// <para>
    /// <b>False for nearly everything, and it has to be, because giving work up cheaply is how a population
    /// stays responsive.</b> A hunt abandoned halfway has cost some walking; a dig abandoned halfway has
    /// cost some swings. Nothing is owed to either and the auction is right to move on. The exception is
    /// work with a purchase in the middle of it: <c>BotSupply</c> draws the guild's money, buys a lot at a
    /// shop, and only then sets off for the hall, so an errand dropped after the first leg has converted
    /// the guild's coin into goods in a courier's pack and delivered none of it.
    /// </para>
    ///
    /// <para>
    /// Measured 09.09.2026, and it was a defect of mine hours old. Making the carrying ceiling a rank rather
    /// than a discount (see <c>BotWill.Auction</c>) meant a courier that went over its ceiling <em>by
    /// buying the lot</em> immediately had the errand taken off it by the unloading, which then sold the
    /// guild's goods where it stood. Supply errands dropped after the money was spent went from ten in the
    /// hour before to eighty in the hour after, thirty-eight of them to the unloading, and three hundred and
    /// sixty sewing kits were bought to put two hundred and eighteen on a shelf.
    /// </para>
    ///
    /// <para>
    /// The cure has two halves and this is the second. The first is not to send a courier for more than it
    /// can carry, which is where the state should never arise (<c>BotSupplier.Fits</c>); this is what
    /// happens when it arises anyway. It does not make the work immortal - see <c>BotDeed.Pressing</c>,
    /// which still takes precedence, as does the carrying ceiling itself, because a bot that cannot walk
    /// cannot deliver either and nothing is served by holding it to a promise it cannot keep.
    /// </para>
    /// </summary>
    public virtual bool Committed => false;

    /// <summary>
    /// Whether standing perfectly still, in one place, with the same thing to say about itself, is what this
    /// work looks like when it is going <em>well</em>.
    ///
    /// <para>
    /// <b>The stall watch tests two things and this is the answer to one of them.</b> It asks whether the
    /// bot has moved and whether its <see cref="Stage"/> has changed, and takes the work off it when neither
    /// has for four minutes — which is right for a walk to somewhere unreachable and exactly wrong for a
    /// drill field. On 03.09.2026 it cancelled eight lessons: five students standing in their ranks, whose
    /// stage says how many points they have picked up to one decimal place and therefore does not move at
    /// all while the last few tenths of a skill come in at a hundredth a beat, and three captains calling a
    /// class that nobody had yet joined. Every one of those students had already paid its fee.
    /// </para>
    ///
    /// <para>
    /// <b>A display string is not a progress measure, and that is the whole lesson.</b> Rounding is a fact
    /// about the sentence, not about the work. Rather than widen the decimal — which moves the boundary and
    /// keeps the mistake — work that stands still on purpose says so, and is left to the clock it already
    /// has: a lesson runs out at <c>BotSchool.LessonMs</c>, a roll closes at <c>GatherMs</c>. Whoever set a
    /// clock owns ending the thing, which is the same rule the companies were taught.
    /// </para>
    /// </summary>
    public virtual bool Still => false;

    /// <summary>
    /// This undertaking is over, however it ended. Release whatever was being held for it — a claimed vein,
    /// a reserved order — and nothing else; the takings are counted by the brain.
    /// </summary>
    public virtual void Drop(IBotWilful bot)
    {
    }

    /// <summary>
    /// Whether the bot should run while doing this, or walk.
    ///
    /// <para>
    /// <b>True for everything, and the one exception is the reason it exists.</b> Running is otherwise a
    /// property of the population — a setting read once, tempered only by whether the bot has the breath for
    /// it — and that is right for almost all of this: a bot crossing half a map to a vein, to a fight, to a
    /// counter is a bot whose errand is at the far end and whose walk is pure cost. But an errand whose whole
    /// content <em>is</em> the walking looks absurd taken at a sprint, and looking absurd is a real fault on a
    /// shard somebody watches from a client: a Baron sprinting laps of his own town reads as a bot with a bug,
    /// not as a bot at rest.
    /// </para>
    ///
    /// <para>
    /// Asked of the undertaking rather than set on the bot, because the bot is the wrong owner of the answer —
    /// it changes with what is being done and it has to change back by itself. See <c>BotMobile.Running</c>,
    /// which still has the last word: breath beats intent, and nothing here can make a tired bot run.
    /// </para>
    /// </summary>
    public virtual bool Hurries => true;

    /// <summary>Which stage it is on, in words, for the log. Null when there is nothing useful to say.</summary>
    public virtual string Stage => null;

    public override string ToString()
    {
        var stage = Stage;

        return stage == null ? Kind : $"{Kind}: {stage}";
    }
}
