using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SnowballArena
{
    public enum MatchPhase { Menu, Countdown, Running, Victory, Results }
    public enum MenuPage { Home, Tutorial, Opponents }
    public enum EliminationCause { None, Fall, Cannon }
    public sealed class MatchParticipant
    {
        public SnowballBody body;
        public SnowballMotor motor;
        public bool alive = true;
        public string displayName;
        public bool isPlayer;
        public EliminationCause eliminationCause;
    }

    public sealed class MatchController : MonoBehaviour
    {
        public GameObject snowballPrefab;
        public ArenaSurface surface;
        public Camera arenaCamera;
        public int botCount = 5;
        public float countdownSeconds = 3f;
        [Tooltip("Seconds after the last shove during which a fall still counts as the player's kill.")]
        public float knockdownCreditSeconds = 3.5f;
        [Tooltip("Seconds after the player wins that they can still drive around before the panel opens.")]
        [Min(0f)] public float victorySeconds = 3f;
        [Tooltip("Play the dark-stage spotlight beat before the results panel. Automation turns this off.")]
        public bool celebrateVictory = true;
        public MatchPhase Phase { get; private set; } = MatchPhase.Menu;
        public MenuPage Page { get; private set; } = MenuPage.Home;
        public List<MatchParticipant> Participants { get; } = new List<MatchParticipant>();
        public int Remaining { get; private set; }
        public string Result { get; private set; }
        public string Notice { get; private set; }
        public float Countdown { get; private set; }
        public CannonDirector Cannon { get; private set; }
        private int lastCountdownSecond = -1;
        private float victoryUntil;
        private Font font;
        private WinterMenuView winterMenu;
        private void Awake()
        {
            Cannon = GetComponent<CannonDirector>();
            if (Cannon == null) Cannon = gameObject.AddComponent<CannonDirector>();
            Cannon.match = this;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) ReturnToMenu();
                if (Phase == MatchPhase.Menu)
                {
                    if (Page == MenuPage.Opponents)
                    {
                        if (keyboard.leftArrowKey.wasPressedThisFrame) { botCount = Mathf.Max(0, botCount - 1); Click(); }
                        if (keyboard.rightArrowKey.wasPressedThisFrame) { botCount = Mathf.Min(15, botCount + 1); Click(); }
                    }
                    if (keyboard.enterKey.wasPressedThisFrame) AdvanceMenu();
                }
                else if (keyboard.rKey.wasPressedThisFrame && (Phase == MatchPhase.Results || Participants.Count > 0 && !Participants[0].alive)) StartMatch(botCount);
            }
            if (Phase == MatchPhase.Victory && Time.time >= victoryUntil)
            {
                // The stage beat is over: park the winner so the panel is read against a still frame.
                Phase = MatchPhase.Results;
                foreach (var participant in Participants)
                {
                    participant.motor.controlEnabled = false;
                    if (participant.alive) participant.body.RigidBody.isKinematic = true;
                }
            }
            if (Phase == MatchPhase.Countdown)
            {
                Countdown -= Time.deltaTime;
                int second = Mathf.CeilToInt(Mathf.Max(0f, Countdown));
                if (second != lastCountdownSecond) { lastCountdownSecond = second; Tick(); }
                if (Countdown <= 0) BeginRunning();
            }
        }

        private static void Click() { if (ArenaFeedback.Instance != null) ArenaFeedback.Instance.UiClick(); }
        private static void Tick() { if (ArenaFeedback.Instance != null) ArenaFeedback.Instance.UiTick(); }

        public bool StartMatch(int bots)
        {
            bots = Mathf.Clamp(bots, 0, 15);
            var settings = snowballPrefab.GetComponent<SnowballBody>().settings;
            if (!SpawnService.TryGetPositions(surface, bots + 1, settings.initialRadius, out var spawns))
            { Notice = "平台空间不足，请减少机器人数。"; return false; }
            ClearParticipants(); botCount = bots; Notice = null; Result = null;
            for (int i = 0; i <= bots; i++)
            {
                var instance = Instantiate(snowballPrefab, spawns[i], Quaternion.identity);
                instance.name = i == 0 ? "Player" : "Bot " + i;
                var body = instance.GetComponent<SnowballBody>();
                var motor = instance.GetComponent<SnowballMotor>();
                var participant = new MatchParticipant { body = body, motor = motor, isPlayer = i == 0, displayName = i == 0 ? "你" : "雪球 " + i };
                body.isPlayer = i == 0;
                Participants.Add(participant);
                instance.GetComponent<SnowballAppearance>()?.SetIdentity(i);
                var input = instance.GetComponent<PlayerInputSource>();
                if (i == 0) input.cameraFrame = arenaCamera.transform;
                else
                {
                    input.enabled = false;
                    var brain = instance.AddComponent<BotBrain>();
                    brain.match = this; brain.self = participant; brain.personality = i * 1.731f;
                }
                motor.controlEnabled = false;
                instance.GetComponent<SnowballGrowth>().enabled = false;
            }
            Remaining = Participants.Count; Countdown = countdownSeconds; lastCountdownSecond = -1; Phase = MatchPhase.Countdown;
            if (ArenaFeedback.Instance != null) ArenaFeedback.Instance.UiConfirm();
            return true;
        }

        public void BeginRunning()
        {
            if (Phase != MatchPhase.Countdown) return;
            Phase = MatchPhase.Running;
            foreach (var participant in Participants)
            {
                participant.motor.controlEnabled = true;
                participant.body.GetComponent<SnowballGrowth>().enabled = true;
                participant.body.GetComponent<SnowballGrowth>().ResetTracking();
            }
        }

        private void FixedUpdate()
        {
            if (Phase == MatchPhase.Running) Cannon.Advance(Time.fixedDeltaTime);
            ResolveEliminations();
        }
        public void Eliminate(MatchParticipant participant, EliminationCause cause)
        {
            if (Phase != MatchPhase.Running || participant == null || !participant.alive) return;
            participant.alive = false; participant.motor.controlEnabled = false;
            participant.eliminationCause = cause;
            participant.body.GetComponent<SnowballGrowth>().enabled = false;
            participant.body.gameObject.SetActive(false);
            if (participant.isPlayer) Notice = cause == EliminationCause.Cannon ? "你被炮弹击中" : "你掉出了平台";
            else if (cause == EliminationCause.Fall) CreditKnockdown(participant);
        }

        // A fall only counts as the player's kill when the player was the last ball pushing it
        // and that shove is recent enough to be the cause. Cannon kills and bot-on-bot falls
        // stay uncredited, so the reward cue keeps meaning "I pushed that one off".
        private void CreditKnockdown(MatchParticipant victim)
        {
            var attacker = victim.body.LastKnockedBy;
            if (attacker == null) return;
            if (Time.fixedTime - victim.body.LastKnockTime > knockdownCreditSeconds) return;
            foreach (var owner in Participants)
            {
                if (owner.body != attacker) continue;
                if (!owner.isPlayer || !owner.alive) return;
                if (ArenaFeedback.Instance != null) ArenaFeedback.Instance.Knockdown();
                Notice = "撞落 " + victim.displayName;
                return;
            }
        }
        public void ResolveEliminations()
        {
            if (Phase != MatchPhase.Running) return;
            Remaining = 0; MatchParticipant survivor = null;
            // Collect this physics step's eliminations before resolving the winner.
            foreach (var participant in Participants)
            {
                if (participant.alive && participant.body.RigidBody.position.y < surface.definition.fallHeight)
                {
                    Eliminate(participant, EliminationCause.Fall);
                }
                if (participant.alive) { Remaining++; survivor = participant; }
            }
            bool survivorOnPlatform = survivor != null && ArenaQuery.TrySupport(gameObject.scene.GetPhysicsScene(),
                survivor.body.RigidBody.position, survivor.body.Radius, survivor.motor.supportLayers,
                survivor.body.settings.supportProbeGap, survivor.body.settings.minimumSupportNormal, out _);
            if (Remaining == 0 || botCount > 0 && Remaining == 1 && survivorOnPlatform)
            {
                bool playerWon = Remaining == 1 && survivor != null && survivor.isPlayer;
                Result = Remaining == 0 ? (botCount == 0 ? "练习结束" : "平局") : survivor.isPlayer ? "你赢了！" : survivor.displayName + " 获胜";
                // The player's win gets a short stage beat first: the valley goes dark, a
                // spotlight follows the winner, and the panel only opens once it has played out.
                // Headless runs skip the stage beat: automation should see the same phase
                // transitions as before, and there is nothing to light up in batch mode.
                bool celebrate = playerWon && celebrateVictory && victorySeconds > 0f;
                Phase = celebrate ? MatchPhase.Victory : MatchPhase.Results;
                Cannon.Stop();
                if (ArenaFeedback.Instance != null) ArenaFeedback.Instance.Result(playerWon);
                foreach (var participant in Participants)
                {
                    participant.body.GetComponent<SnowballGrowth>().enabled = false;
                    if (participant == survivor) continue;
                    participant.motor.controlEnabled = false;
                    // Freeze only after the match ends so the displayed winner stays on screen.
                    if (participant.alive) participant.body.RigidBody.isKinematic = true;
                }
                if (celebrate)
                {
                    victoryUntil = Time.time + Mathf.Max(0f, victorySeconds);
                    VictoryStage.Begin(survivor.body.transform);
                }
                else if (survivor != null) survivor.motor.controlEnabled = false;
            }
        }

        public void AdvanceMenu()
        {
            if (Phase != MatchPhase.Menu) return;
            if (Page == MenuPage.Home) Page = MenuPage.Tutorial;
            else if (Page == MenuPage.Tutorial) Page = MenuPage.Opponents;
            else { StartMatch(botCount); return; }
            Click();
        }

        public void ReturnToMenu() { VictoryStage.End(); ClearParticipants(); Phase = MatchPhase.Menu; Page = MenuPage.Home; Notice = null; Click(); }
        private void ClearParticipants()
        {
            VictoryStage.End();
            if (Cannon != null) Cannon.ResetRound();
            foreach (var participant in Participants)
                if (participant.body != null) { participant.body.gameObject.SetActive(false); Destroy(participant.body.gameObject); }
            Participants.Clear(); Remaining = 0;
        }

        private void OnGUI()
        {
            if (winterMenu == null)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Segoe UI" }, 24);
                winterMenu = new WinterMenuView(font);
            }
            Matrix4x4 previous = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float w = Screen.width / scale, h = Screen.height / scale;
            if (Phase == MatchPhase.Menu)
            {
                int previousCount = botCount;
                bool start = winterMenu.Draw(w, h, ref botCount, Notice, Page);
                if (botCount != previousCount) Click();
                if (start) AdvanceMenu();
            }
            else
            {
                if (Phase == MatchPhase.Countdown)
                {
                    winterMenu.Panel(new Rect(w / 2 - 68, h / 2 - 68, 136, 136));
                    GUI.Label(new Rect(w / 2 - 54, h / 2 - 54, 108, 108), Mathf.CeilToInt(Countdown).ToString(), winterMenu.Countdown);
                }
                if (Phase == MatchPhase.Results)
                {
                    winterMenu.DimBackground(w, h);
                    var r = new Rect(w / 2 - 240, h / 2 - 140, 480, 280);
                    winterMenu.Panel(r);
                    GUI.Label(new Rect(r.x + 30, r.y + 29, 420, 66), Result, winterMenu.ResultTitle);
                    if (GUI.Button(new Rect(r.x + 36, r.y + 121, 408, 56), "再来一局  ·  R", winterMenu.PrimaryButton)) StartMatch(botCount);
                    if (GUI.Button(new Rect(r.x + 36, r.y + 193, 408, 52), "返回菜单  ·  Esc", winterMenu.SecondaryButton)) ReturnToMenu();
                }
            }
            GUI.matrix = previous;
        }

        private void OnDestroy()
        {
            winterMenu?.Dispose();
            if (font != null) Destroy(font);
        }
    }
}
