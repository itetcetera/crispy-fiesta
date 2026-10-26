namespace Mule.Engine.Flow;

/// <summary>Something the client shows or plays. Events carry no rules: the server state is the truth.</summary>
public abstract record GameEvent;

public sealed record TitleEvent(string Title) : GameEvent;
public sealed record MessageEvent(string Text) : GameEvent;
public sealed record MapShownEvent : GameEvent;
public sealed record SoundEvent(int Sound) : GameEvent;

// Land grant
public sealed record GrantCursorEvent(int Plot) : GameEvent;
public sealed record GrantCursorHiddenEvent : GameEvent;
public sealed record PlotGrantedEvent(int Plot, int Player) : GameEvent;

// Land auction
public sealed record AuctionStartedEvent(int Plot, int Price) : GameEvent;
public sealed record AuctionPriceEvent(int Price) : GameEvent;
public sealed record AuctionStateEvent(byte[] Positions, byte Leader, ushort High, byte Time) : GameEvent;
public sealed record AuctionEndedEvent(int Winner, int Price) : GameEvent;

// Goods auction
public sealed record GoodsStatusEvent(int Good) : GameEvent;
/// <summary>The status bars move from <paramref name="From"/> to <paramref name="To"/> (quarter units): 1 usage, 2 spoilage, 3 production.</summary>
public sealed record GoodsBarsEvent(int Kind, ushort[] From, ushort[] To) : GameEvent;
/// <summary>New requirements; <paramref name="Kinds"/>: 0 surplus, 12 shortage per player.</summary>
public sealed record GoodsRequirementEvent(byte[] Required, byte[] Kinds) : GameEvent;
/// <summary>Who is buying and who is selling (1 = seller).</summary>
public sealed record GoodsSelectEvent(int Good, byte[] Sellers) : GameEvent;
/// <summary>The goods auction's trading starts: the price scale and who sells.</summary>
public sealed record GoodsAuctionStartedEvent(int Good, ushort PriceStart, ushort PriceEnd, byte[] Sellers, bool StoreSells) : GameEvent;
/// <summary>One step of the goods auction: heights, the best buyer and seller ($FF the store) and the time bar.</summary>
public sealed record GoodsAuctionStateEvent(byte[] Positions, byte Buyer, byte Seller, ushort High, ushort Low, byte Time) : GameEvent;
/// <summary>One unit changes hands at a price; a side of $FF is the store.</summary>
public sealed record GoodsTradedEvent(byte Buyer, byte Seller, ushort Price) : GameEvent;
/// <summary>The goods auction is over; the good's new price.</summary>
public sealed record GoodsAuctionEndedEvent(int Good, ushort Price) : GameEvent;

// Development turns
public sealed record TurnStartedEvent(int Player) : GameEvent;
public sealed record TurnEndedEvent(int Player) : GameEvent;
public sealed record PlayerEventShownEvent(Mule.Engine.Rules.PlayerEventResult Result) : GameEvent;
/// <summary>A turn message by its number in the original's table ($3B6F), or other text; code 0 and no text clears it.</summary>
public sealed record TurnMessageEvent(int Code, string Text) : GameEvent;
public sealed record TurnScreenEvent(bool Map) : GameEvent;
public sealed record MuleBoughtEvent(int Player) : GameEvent;
public sealed record MuleSoldBackEvent(int Player) : GameEvent;
public sealed record MuleOutfittedEvent(int Player, int Outfit) : GameEvent;
public sealed record MuleRanAwayEvent(int Player) : GameEvent;
public sealed record PlotChangedEvent(int Plot, int Exp) : GameEvent;
public sealed record AssayEvent(int Plot, int Level) : GameEvent;
public sealed record GamblingEvent(int Player, int Won) : GameEvent;
/// <summary>A CPU player's cursor on the plot it just outfitted or assayed.</summary>
public sealed record CpuActionEvent(int Player, int Plot, bool Assay) : GameEvent;

/// <summary>The wampus shows on (or leaves) its mountain at x, y.</summary>
public sealed record WampusEvent(bool Shown, byte X, byte Y) : GameEvent;

/// <summary>A player caught the wampus and got the bonus.</summary>
public sealed record WampusCaughtEvent(byte Player, ushort Bonus) : GameEvent;
