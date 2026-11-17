namespace SliceNServe.Pvp
{
    public enum MatchMode
    {
        /// <summary>The normal single-player run.</summary>
        Solo = 0,
        /// <summary>"Play with Friends": two players, two boards, one screen.</summary>
        LocalVersus = 1,
        /// <summary>"Enter PvP match": two devices on the same network, one board each.</summary>
        Online = 2
    }

    /// <summary>
    /// What the next scene load should be. The menu (or lobby) fills it in and reloads the scene;
    /// <see cref="PvpRunner"/> reads it on start. Static so it survives the reload, like the open
    /// connection it carries. Back to <see cref="MatchMode.Solo"/> with <see cref="Clear"/>.
    /// </summary>
    public static class MatchSetup
    {
        public const float DefaultDurationSeconds = 180f;

        public static MatchMode Mode { get; private set; } = MatchMode.Solo;
        public static int Seed { get; private set; }
        public static float DurationSeconds { get; private set; } = DefaultDurationSeconds;

        /// <summary>Online only: the open connection, and whether this device hosts the match.</summary>
        public static IMatchTransport Transport { get; private set; }
        public static bool IsHost { get; private set; }

        public static bool IsPvp => Mode != MatchMode.Solo;

        /// <summary>Play with Friends → Play with CPU: player 2 is the auto-player.</summary>
        public static bool VsCpu { get; private set; }

        public static void BeginLocal(int seed, float duration = DefaultDurationSeconds, bool vsCpu = false)
        {
            Close();
            Mode = MatchMode.LocalVersus;
            Seed = seed;
            DurationSeconds = duration;
            VsCpu = vsCpu;
        }

        public static void BeginOnline(IMatchTransport transport, bool isHost, int seed, float duration = DefaultDurationSeconds)
        {
            if (Transport != transport) Close();
            Mode = MatchMode.Online;
            VsCpu = false;
            Transport = transport;
            IsHost = isHost;
            Seed = seed;
            DurationSeconds = duration;
        }

        /// <summary>Back to solo play, closing any connection.</summary>
        public static void Clear()
        {
            Close();
            Mode = MatchMode.Solo;
            VsCpu = false;
        }

        private static void Close()
        {
            Transport?.Close();
            Transport = null;
            IsHost = false;
        }
    }
}
