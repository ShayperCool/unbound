// The Social API with Unity's local platform (`UnityEngine.SocialPlatforms.Local`), which Unity
// uses wherever no platform service is plugged in (everywhere but iOS): authentication succeeds
// with a local user, scores and achievements are kept in memory.
#pragma warning disable CS0618 // Unity marks the Social API deprecated.
using System;
using System.Collections.Generic;
using UnityEngine.SocialPlatforms;
using UnityEngine.SocialPlatforms.Impl;

namespace UnityEngine
{
    [Obsolete("Social is deprecated and will be removed in a future release.")]
    public static partial class Social
    {
        public static ISocialPlatform Active
        {
            get => ActivePlatform.Instance;
            set => ActivePlatform.Instance = value;
        }

        public static ILocalUser localUser => Active.localUser;

        public static void LoadUsers(string[] userIDs, Action<IUserProfile[]> callback) => Active.LoadUsers(userIDs, callback);
        public static void ReportProgress(string achievementID, double progress, Action<bool> callback) => Active.ReportProgress(achievementID, progress, callback);
        public static void LoadAchievementDescriptions(Action<IAchievementDescription[]> callback) => Active.LoadAchievementDescriptions(callback);
        public static void LoadAchievements(Action<IAchievement[]> callback) => Active.LoadAchievements(callback);
        public static void ReportScore(long score, string board, Action<bool> callback) => Active.ReportScore(score, board, callback);
        public static void LoadScores(string leaderboardID, Action<IScore[]> callback) => Active.LoadScores(leaderboardID, callback);
        public static ILeaderboard CreateLeaderboard() => Active.CreateLeaderboard();
        public static IAchievement CreateAchievement() => Active.CreateAchievement();
        public static void ShowAchievementsUI() => Active.ShowAchievementsUI();
        public static void ShowLeaderboardUI() => Active.ShowLeaderboardUI();
    }
}

namespace UnityEngine.SocialPlatforms
{
    [Obsolete("IAchievement is deprecated and will be removed in a future release.")]
    public partial interface IAchievement
    {
        void ReportProgress(Action<bool> callback);
        string id { get; set; }
        double percentCompleted { get; set; }
        bool completed { get; }
        bool hidden { get; }
        DateTime lastReportedDate { get; }
    }
    [Obsolete("IAchievementDescription is deprecated and will be removed in a future release.")]
    public partial interface IAchievementDescription
    {
        string id { get; set; }
        string title { get; }
        Texture2D image { get; }
        string achievedDescription { get; }
        string unachievedDescription { get; }
        bool hidden { get; }
        int points { get; }
    }
    [Obsolete("ILeaderboard is deprecated and will be removed in a future release.")]
    public partial interface ILeaderboard
    {
        void SetUserFilter(string[] userIDs);
        void LoadScores(Action<bool> callback);
        bool loading { get; }
        string id { get; set; }
        UserScope userScope { get; set; }
        Range range { get; set; }
        TimeScope timeScope { get; set; }
        IScore localUserScore { get; }
        uint maxRange { get; }
        IScore[] scores { get; }
        string title { get; }
    }
    [Obsolete("ILocalUser is deprecated and will be removed in a future release.")]
    public partial interface ILocalUser : IUserProfile
    {
        void Authenticate(Action<bool> callback);
        void Authenticate(Action<bool, string> callback);
        void LoadFriends(Action<bool> callback);
        IUserProfile[] friends { get; }
        bool authenticated { get; }
        bool underage { get; }
    }
    [Obsolete("IScore is deprecated and will be removed in a future release.")]
    public partial interface IScore
    {
        void ReportScore(Action<bool> callback);
        string leaderboardID { get; set; }
        long value { get; set; }
        DateTime date { get; }
        string formattedValue { get; }
        string userID { get; }
        int rank { get; }
    }
    [Obsolete("ISocialPlatform is deprecated and will be removed in a future release.")]
    public partial interface ISocialPlatform
    {
        void LoadUsers(string[] userIDs, Action<IUserProfile[]> callback);
        void ReportProgress(string achievementID, double progress, Action<bool> callback);
        void LoadAchievementDescriptions(Action<IAchievementDescription[]> callback);
        void LoadAchievements(Action<IAchievement[]> callback);
        IAchievement CreateAchievement();
        void ReportScore(long score, string board, Action<bool> callback);
        void LoadScores(string leaderboardID, Action<IScore[]> callback);
        ILeaderboard CreateLeaderboard();
        void ShowAchievementsUI();
        void ShowLeaderboardUI();
        void Authenticate(ILocalUser user, Action<bool> callback);
        void Authenticate(ILocalUser user, Action<bool, string> callback);
        void LoadFriends(ILocalUser user, Action<bool> callback);
        void LoadScores(ILeaderboard board, Action<bool> callback);
        bool GetLoading(ILeaderboard board);
        ILocalUser localUser { get; }
    }
    [Obsolete("IUserProfile is deprecated and will be removed in a future release.")]
    public partial interface IUserProfile
    {
        string userName { get; }
        string id { get; }
        bool isFriend { get; }
        UserState state { get; }
        Texture2D image { get; }
    }
    [Obsolete("TimeScope is deprecated and will be removed in a future release.")]
    public enum TimeScope
    {
        Today = 0,
        Week = 1,
        AllTime = 2,
    }
    [Obsolete("UserScope is deprecated and will be removed in a future release.")]
    public enum UserScope
    {
        Global = 0,
        FriendsOnly = 1,
    }
    [Obsolete("UserState is deprecated and will be removed in a future release.")]
    public enum UserState
    {
        Online = 0,
        OnlineAndAway = 1,
        OnlineAndBusy = 2,
        Offline = 3,
        Playing = 4,
    }

    [Obsolete("ActivePlatform is deprecated and will be removed in a future release.")]
    internal static partial class ActivePlatform
    {
        static ISocialPlatform s_Active;

        internal static ISocialPlatform Instance
        {
            get => s_Active ??= new Local();
            set => s_Active = value;
        }
    }

    [Obsolete("Range is deprecated and will be removed in a future release.")]
    public partial struct Range
    {
        public int from;
        public int count;

        public Range(int fromValue, int valueCount)
        {
            from = fromValue;
            count = valueCount;
        }
    }

    [Obsolete("Local is deprecated and will be removed in a future release.")]
    public partial class Local : ISocialPlatform
    {
        static LocalUser m_LocalUser;
        readonly List<UserProfile> m_Friends = new List<UserProfile>();
        readonly List<UserProfile> m_Users = new List<UserProfile>();
        readonly List<AchievementDescription> m_AchievementDescriptions = new List<AchievementDescription>();
        readonly List<Achievement> m_Achievements = new List<Achievement>();
        readonly List<Leaderboard> m_Leaderboards = new List<Leaderboard>();
        Texture2D m_DefaultTexture;

        public Local() { }

        public ILocalUser localUser => m_LocalUser ??= new LocalUser();

        void ISocialPlatform.Authenticate(ILocalUser user, Action<bool> callback)
        {
            var thisUser = (LocalUser)user;
            m_DefaultTexture ??= new Texture2D(32, 32);
            PopulateStaticData();
            thisUser.SetAuthenticated(true);
            thisUser.SetUnderage(false);
            thisUser.SetUserID("1000");
            thisUser.SetUserName("Lerpz");
            thisUser.SetImage(m_DefaultTexture);
            callback?.Invoke(true);
        }

        void ISocialPlatform.Authenticate(ILocalUser user, Action<bool, string> callback) =>
            ((ISocialPlatform)this).Authenticate(user, success => callback?.Invoke(success, null));

        void ISocialPlatform.LoadFriends(ILocalUser user, Action<bool> callback)
        {
            if (!VerifyUser()) return;
            ((LocalUser)user).SetFriends(m_Friends.ToArray());
            callback?.Invoke(true);
        }

        public void LoadUsers(string[] userIDs, Action<IUserProfile[]> callback)
        {
            var users = new List<UserProfile>();
            if (!VerifyUser()) return;
            foreach (var id in userIDs)
                foreach (var u in m_Users)
                    if (u.id == id) users.Add(u);
            callback?.Invoke(users.ToArray());
        }

        public void ReportProgress(string id, double progress, Action<bool> callback)
        {
            if (!VerifyUser()) return;
            foreach (var a in m_Achievements)
            {
                if (a.id == id && a.percentCompleted <= progress)
                {
                    if (progress >= 100.0) a.SetCompleted(true);
                    a.SetHidden(false);
                    a.SetLastReportedDate(DateTime.Now);
                    a.percentCompleted = progress;
                    callback?.Invoke(true);
                    return;
                }
            }
            foreach (var d in m_AchievementDescriptions)
            {
                if (d.id == id)
                {
                    m_Achievements.Add(new Achievement(id, progress, progress >= 100.0, false, DateTime.Now));
                    callback?.Invoke(true);
                    return;
                }
            }
            Debug.LogError("Achievement ID not found");
            callback?.Invoke(false);
        }

        public void LoadAchievementDescriptions(Action<IAchievementDescription[]> callback)
        {
            if (!VerifyUser()) return;
            callback?.Invoke(m_AchievementDescriptions.ToArray());
        }

        public void LoadAchievements(Action<IAchievement[]> callback)
        {
            if (!VerifyUser()) return;
            callback?.Invoke(m_Achievements.ToArray());
        }

        public void ReportScore(long score, string board, Action<bool> callback)
        {
            if (!VerifyUser()) return;
            foreach (var l in m_Leaderboards)
            {
                if (l.id != board) continue;
                var scores = new List<IScore>(l.scores) { new Score(board, score, localUser.id, DateTime.Now, score + " points", 0) };
                l.SetScores(scores.ToArray());
                callback?.Invoke(true);
                return;
            }
            Debug.LogError("Leaderboard not found");
            callback?.Invoke(false);
        }

        public void LoadScores(string leaderboardID, Action<IScore[]> callback)
        {
            if (!VerifyUser()) return;
            foreach (var l in m_Leaderboards)
            {
                if (l.id != leaderboardID) continue;
                SortScores(l);
                callback?.Invoke(l.scores);
                return;
            }
            Debug.LogError("Leaderboard not found");
            callback?.Invoke(new Score[0]);
        }

        void ISocialPlatform.LoadScores(ILeaderboard board, Action<bool> callback)
        {
            if (!VerifyUser()) return;
            var thisBoard = (Leaderboard)board;
            foreach (var l in m_Leaderboards)
            {
                if (l.id != thisBoard.id) continue;
                thisBoard.SetTitle(l.title);
                thisBoard.SetScores(l.scores);
                thisBoard.SetMaxRange((uint)l.scores.Length);
            }
            SortScores(thisBoard);
            SetLocalPlayerScore(thisBoard);
            callback?.Invoke(true);
        }

        bool ISocialPlatform.GetLoading(ILeaderboard board)
        {
            if (!VerifyUser()) return false;
            return ((Leaderboard)board).loading;
        }

        static void SortScores(Leaderboard board)
        {
            var scores = new List<IScore>(board.scores);
            scores.Sort((a, b) => b.value.CompareTo(a.value));
            for (int i = 0; i < scores.Count; i++)
                if (scores[i] is Score score) score.SetRank(i + 1);
            board.SetScores(scores.ToArray());
        }

        void SetLocalPlayerScore(Leaderboard board)
        {
            foreach (var s in board.scores)
            {
                if (s.userID == localUser.id)
                {
                    board.SetLocalUserScore(s);
                    break;
                }
            }
        }

        public void ShowAchievementsUI() => Debug.Log("ShowAchievementsUI not implemented");

        public void ShowLeaderboardUI() => Debug.Log("ShowLeaderboardUI not implemented");

        public ILeaderboard CreateLeaderboard() => new Leaderboard();

        public IAchievement CreateAchievement() => new Achievement();

        bool VerifyUser()
        {
            if (!localUser.authenticated)
            {
                Debug.LogError("Must authenticate first");
                return false;
            }
            return true;
        }

        void PopulateStaticData()
        {
            if (m_Friends.Count > 0) return;
            m_Friends.Add(new UserProfile("Fred", "1001", true, UserState.Online, m_DefaultTexture));
            m_Friends.Add(new UserProfile("Julia", "1002", true, UserState.Online, m_DefaultTexture));
            m_Friends.Add(new UserProfile("Jeff", "1003", true, UserState.Online, m_DefaultTexture));
            m_Users.AddRange(m_Friends);
            m_Users.Add(new UserProfile("Sam", "1004", false, UserState.Offline, m_DefaultTexture));
            m_Users.Add(new UserProfile("Max", "1005", false, UserState.Offline, m_DefaultTexture));
            m_AchievementDescriptions.Add(new AchievementDescription("Achievement01", "First achievement", m_DefaultTexture, "Get first achievement", "Received first achievement", false, 10));
            m_AchievementDescriptions.Add(new AchievementDescription("Achievement02", "Second achievement", m_DefaultTexture, "Get second achievement", "Received second achievement", false, 20));
            m_AchievementDescriptions.Add(new AchievementDescription("Achievement03", "Third achievement", m_DefaultTexture, "Get third achievement", "Received third achievement", false, 15));
            var board = new Leaderboard();
            board.SetTitle("High Scores");
            board.id = "Leaderboard01";
            board.SetScores(new IScore[]
            {
                new Score("Leaderboard01", 300, "1001", DateTime.Now.AddDays(-1), "300 points", 1),
                new Score("Leaderboard01", 255, "1002", DateTime.Now.AddDays(-1), "255 points", 2),
                new Score("Leaderboard01", 55, "1003", DateTime.Now.AddDays(-1), "55 points", 3),
                new Score("Leaderboard01", 10, "1004", DateTime.Now.AddDays(-1), "10 points", 4),
            });
            m_Leaderboards.Add(board);
        }
    }
}

namespace UnityEngine.SocialPlatforms.Impl
{
    [Obsolete("UserProfile is deprecated and will be removed in a future release.")]
    public partial class UserProfile : IUserProfile
    {
        protected string m_UserName;
        protected string m_ID;
        protected bool m_IsFriend;
        protected UserState m_State;
        protected Texture2D m_Image;
        string m_LegacyID;
        string m_GameID;

        public UserProfile()
        {
            m_UserName = "Uninitialized";
            m_ID = "0";
            m_LegacyID = "0";
            m_IsFriend = false;
            m_State = UserState.Offline;
        }

        public UserProfile(string name, string id, bool friend) : this(name, id, friend, UserState.Offline, null) { }

        public UserProfile(string name, string id, bool friend, UserState state, Texture2D image) : this(name, id, id, friend, state, image) { }

        public UserProfile(string name, string teamId, string gameId, bool friend, UserState state, Texture2D image)
        {
            m_UserName = name;
            m_ID = teamId;
            m_GameID = gameId;
            m_IsFriend = friend;
            m_State = state;
            m_Image = image;
        }

        public override string ToString() => id + " - " + userName + " - " + isFriend + " - " + state;

        public void SetUserName(string name) => m_UserName = name;
        public void SetUserID(string id) => m_ID = id;
        public void SetLegacyUserID(string id) => m_LegacyID = id;
        public void SetUserGameID(string id) => m_GameID = id;
        public void SetImage(Texture2D image) => m_Image = image;
        public void SetIsFriend(bool value) => m_IsFriend = value;
        public void SetState(UserState state) => m_State = state;

        public string userName => m_UserName;
        public string id => m_ID;
        [Obsolete("legacyId returns playerID from GKPlayer, which became obsolete in iOS 12.4 . id returns playerID for devices running versions before iOS 12.4, and the newer teamPlayerID for later versions. Please use IUserProfile.id or UserProfile.gameId instead.")]
        public string legacyId => m_LegacyID;
        public string gameId => m_GameID;
        public bool isFriend => m_IsFriend;
        public UserState state => m_State;
        public Texture2D image => m_Image;
    }

    [Obsolete("LocalUser is deprecated and will be removed in a future release.")]
    public partial class LocalUser : UserProfile, ILocalUser
    {
        IUserProfile[] m_Friends;
        bool m_Authenticated;
        bool m_Underage;

        public LocalUser()
        {
            m_Friends = new UserProfile[0];
            m_Authenticated = false;
            m_Underage = false;
        }

        public void Authenticate(Action<bool> callback) => ActivePlatform.Instance.Authenticate(this, callback);
        public void Authenticate(Action<bool, string> callback) => ActivePlatform.Instance.Authenticate(this, callback);
        public void LoadFriends(Action<bool> callback) => ActivePlatform.Instance.LoadFriends(this, callback);
        public void SetFriends(IUserProfile[] friends) => m_Friends = friends;
        public void SetAuthenticated(bool value) => m_Authenticated = value;
        public void SetUnderage(bool value) => m_Underage = value;

        public IUserProfile[] friends => m_Friends;
        public bool authenticated => m_Authenticated;
        public bool underage => m_Underage;
    }

    [Obsolete("Achievement is deprecated and will be removed in a future release.")]
    public partial class Achievement : IAchievement
    {
        bool m_Completed;
        bool m_Hidden;
        DateTime m_LastReportedDate;

        public Achievement(string id, double percentCompleted, bool completed, bool hidden, DateTime lastReportedDate)
        {
            this.id = id;
            this.percentCompleted = percentCompleted;
            m_Completed = completed;
            m_Hidden = hidden;
            m_LastReportedDate = lastReportedDate;
        }

        public Achievement(string id, double percent)
        {
            this.id = id;
            percentCompleted = percent;
            m_Hidden = false;
            m_Completed = false;
            m_LastReportedDate = DateTime.MinValue;
        }

        public Achievement() : this("unknown", 0.0) { }

        public override string ToString() => id + " - " + percentCompleted + " - " + completed + " - " + hidden + " - " + lastReportedDate;

        public void ReportProgress(Action<bool> callback) => ActivePlatform.Instance.ReportProgress(id, percentCompleted, callback);
        public void SetCompleted(bool value) => m_Completed = value;
        public void SetHidden(bool value) => m_Hidden = value;
        public void SetLastReportedDate(DateTime date) => m_LastReportedDate = date;

        public string id { get; set; }
        public double percentCompleted { get; set; }
        public bool completed => m_Completed;
        public bool hidden => m_Hidden;
        public DateTime lastReportedDate => m_LastReportedDate;
    }

    [Obsolete("AchievementDescription is deprecated and will be removed in a future release.")]
    public partial class AchievementDescription : IAchievementDescription
    {
        string m_Title;
        Texture2D m_Image;
        string m_AchievedDescription;
        string m_UnachievedDescription;
        bool m_Hidden;
        int m_Points;

        public AchievementDescription(string id, string title, Texture2D image, string achievedDescription, string unachievedDescription, bool hidden, int points)
        {
            this.id = id;
            m_Title = title;
            m_Image = image;
            m_AchievedDescription = achievedDescription;
            m_UnachievedDescription = unachievedDescription;
            m_Hidden = hidden;
            m_Points = points;
        }

        public override string ToString() => id + " - " + title + " - " + achievedDescription + " - " + unachievedDescription + " - " + points + " - " + hidden;

        public void SetImage(Texture2D image) => m_Image = image;

        public string id { get; set; }
        public string title => m_Title;
        public Texture2D image => m_Image;
        public string achievedDescription => m_AchievedDescription;
        public string unachievedDescription => m_UnachievedDescription;
        public bool hidden => m_Hidden;
        public int points => m_Points;
    }

    [Obsolete("Score is deprecated and will be removed in a future release.")]
    public partial class Score : IScore
    {
        DateTime m_Date;
        string m_FormattedValue;
        string m_UserID;
        int m_Rank;

        public Score() : this("unkown_score", -1) { }

        public Score(string leaderboardID, long value) : this(leaderboardID, value, "0", DateTime.Now, "", -1) { }

        public Score(string leaderboardID, long value, string userID, DateTime date, string formattedValue, int rank)
        {
            this.leaderboardID = leaderboardID;
            this.value = value;
            m_UserID = userID;
            m_Date = date;
            m_FormattedValue = formattedValue;
            m_Rank = rank;
        }

        public override string ToString() =>
            "Rank: '" + m_Rank + "' Value: '" + value + "' Category: '" + leaderboardID + "' PlayerID: '" + m_UserID + "' Date: '" + m_Date + "'";

        public void ReportScore(Action<bool> callback) => ActivePlatform.Instance.ReportScore(value, leaderboardID, callback);
        public void SetDate(DateTime date) => m_Date = date;
        public void SetFormattedValue(string value) => m_FormattedValue = value;
        public void SetUserID(string userID) => m_UserID = userID;
        public void SetRank(int rank) => m_Rank = rank;

        public string leaderboardID { get; set; }
        public long value { get; set; }
        public DateTime date => m_Date;
        public string formattedValue => m_FormattedValue;
        public string userID => m_UserID;
        public int rank => m_Rank;
    }

    [Obsolete("Leaderboard is deprecated and will be removed in a future release.")]
    public partial class Leaderboard : ILeaderboard
    {
        bool m_Loading;
        IScore m_LocalUserScore;
        uint m_MaxRange;
        IScore[] m_Scores;
        string m_Title;
        string[] m_UserIDs;

        public Leaderboard()
        {
            id = "Invalid";
            range = new Range(1, 10);
            userScope = UserScope.Global;
            timeScope = TimeScope.AllTime;
            m_Loading = false;
            m_LocalUserScore = new Score("Invalid", 0);
            m_MaxRange = 0;
            m_Scores = new Score[0];
            m_Title = "Invalid";
            m_UserIDs = new string[0];
        }

        public void SetUserFilter(string[] userIDs) => m_UserIDs = userIDs;

        public override string ToString() =>
            "ID: '" + id + "' Title: '" + m_Title + "' Loading: '" + m_Loading + "' Range: [" + range.from + "," + range.count + "] MaxRange: '" + m_MaxRange + "' Scores: '" + m_Scores.Length + "' UserScope: '" + userScope + "' TimeScope: '" + timeScope + "' UserFilter: '" + m_UserIDs.Length;

        public void LoadScores(Action<bool> callback) => ActivePlatform.Instance.LoadScores(this, callback);
        public void SetLocalUserScore(IScore score) => m_LocalUserScore = score;
        public void SetMaxRange(uint maxRange) => m_MaxRange = maxRange;
        public void SetScores(IScore[] scores) => m_Scores = scores;
        public void SetTitle(string title) => m_Title = title;
        public string[] GetUserFilter() => m_UserIDs;

        public bool loading => ActivePlatform.Instance.GetLoading(this);
        public string id { get; set; }
        public UserScope userScope { get; set; }
        public Range range { get; set; }
        public TimeScope timeScope { get; set; }
        public IScore localUserScore => m_LocalUserScore;
        public uint maxRange => m_MaxRange;
        public IScore[] scores => m_Scores;
        public string title => m_Title;
    }
}
