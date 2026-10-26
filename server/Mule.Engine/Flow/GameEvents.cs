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
