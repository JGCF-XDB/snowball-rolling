using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SnowballArena
{
    public enum MatchPhase { Menu, Countdown, Running, Results }
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
        public MatchPhase Phase { get; private set; } = MatchPhase.Menu;
        public List<MatchParticipant> Participants { get; } = new List<MatchParticipant>();
        public int Remaining { get; private set; }
        public string Result { get; private set; }
        public string Notice { get; private set; }
        public float Countdown { get; private set; }
        public CannonDirector Cannon { get; private set; }
        private int lastCountdownSecond = -1;
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
                    if (keyboard.leftArrowKey.wasPressedThisFrame) { botCount = Mathf.Max(0, botCount - 1); Click(); }
                    if (keyboard.rightArrowKey.wasPressedThisFrame) { botCount = Mathf.Min(15, botCount + 1); Click(); }
                    if (keyboard.enterKey.wasPressedThisFrame) StartMatch(botCount);
                }
                else if (keyboard.rKey.wasPressedThisFrame && (Phase == MatchPhase.Results || Participants.Count > 0 && !Participants[0].alive)) StartMatch(botCount);
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
                Result = Remaining == 0 ? (botCount == 0 ? "练习结束" : "平局") : survivor.isPlayer ? "你赢了！" : survivor.displayName + " 获胜";
                Phase = MatchPhase.Results;
                Cannon.Stop();
                if (ArenaFeedback.Instance != null) ArenaFeedback.Instance.Result(Remaining == 1 && survivor != null && survivor.isPlayer);
                foreach (var participant in Participants)
                {
                    participant.motor.controlEnabled = false;
                    participant.body.GetComponent<SnowballGrowth>().enabled = false;
                    // Freeze only after the match ends so the displayed winner stays on screen.
                    if (participant.alive) participant.body.RigidBody.isKinematic = true;
                }
            }
        }

        public void ReturnToMenu() { ClearParticipants(); Phase = MatchPhase.Menu; Notice = null; Click(); }
        private void ClearParticipants()
        {
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
                bool start = winterMenu.Draw(w, h, ref botCount, Notice);
                if (botCount != previousCount) Click();
                if (start) StartMatch(botCount);
            }
            else
            {
                winterMenu.Panel(new Rect(24, 22, 320, 100));
                GUI.Label(new Rect(46, 35, 274, 42), "留在雪山之巅", winterMenu.HudHeading);
                GUI.Label(new Rect(46, 79, 274, 26), "滚动成长 · 碰撞求生", winterMenu.HudSmall);
                string hazardText = Phase == MatchPhase.Results ? "对局结束" : Cannon.ActiveCannonCount > 0 ?
                    $"炮台 {Cannon.ActiveCannonCount} · 预警 {Cannon.WarningCount} / 飞行 {Cannon.ProjectileCount}" :
                    $"炮台将在 {Mathf.CeilToInt(Mathf.Max(0, Cannon.startAfterSeconds - Cannon.Elapsed))} 秒后出现";
                winterMenu.Panel(new Rect(w / 2 - 210, 22, 420, 98));
                GUI.Label(new Rect(w / 2 - 182, 35, 364, 26), $"对局 {Cannon.Elapsed:F0} 秒", winterMenu.HudSmall);
                GUI.Label(new Rect(w / 2 - 186, 65, 372, 36), hazardText, winterMenu.Hazard);
                winterMenu.Panel(new Rect(w - 304, 22, 280, 137));
                GUI.Label(new Rect(w - 282, 36, 236, 42), "场上剩余  " + Remaining, winterMenu.HudHeading);
                var player = Participants[0];
                GUI.Label(new Rect(w - 282, 81, 236, 26), $"半径 {player.body.Radius:F2} m  ·  {player.body.RigidBody.mass:F0} kg", winterMenu.HudSmall);
                GUI.Label(new Rect(w - 282, 110, 236, 26), player.alive ? "红色描边雪球是你" : Notice == "你被炮弹击中" ? "炮弹命中，观战中" : "你已出局，观战中", winterMenu.HudSmall);
                winterMenu.Panel(new Rect(24, h - 86, 570, 62));
                GUI.Label(new Rect(46, h - 72, 526, 32), "WASD 移动   ·   Esc 返回菜单   ·   出局后 R 重开", winterMenu.HudSmall);
                if (Phase != MatchPhase.Results)
                {
                    winterMenu.Panel(new Rect(w - 264, h - 92, 240, 68));
                    if (GUI.Button(new Rect(w - 254, h - 82, 220, 48), "返回菜单  ·  Esc", winterMenu.SecondaryButton)) ReturnToMenu();
                }
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
