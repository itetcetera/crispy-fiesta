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
