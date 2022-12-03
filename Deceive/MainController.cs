using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using Deceive.Properties;

namespace Deceive { 

internal class MainController : ApplicationContext
{
    internal MainController()
    {
        TrayIcon = new NotifyIcon
        {
            Icon = Resources.DeceiveIcon,
            Visible = true,
            BalloonTipTitle = StartupHandler.DeceiveTitle,
            BalloonTipText = "Deceive is currently masking your status. Right-click the tray icon for more options."
        };
        TrayIcon.ShowBalloonTip(5000);
        Rank = "Default";
        Ranks = new Dictionary<string, int[]>(){
            {"Iron", new int[] {3,4,5}},
            {"Bronze", new int[] {6,7,8}},
            {"Silver", new int[] {9,10,11}},
            {"Gold", new int[] {12,13,14}},
            {"Platinum", new int[] {15,16,17}},
            {"Diamond", new int[] {18,19,20}},
            {"Ascendant", new int[] {21,22,23}},
            {"Immortal", new int[] {24,25,26}},
            {"Radiant", new int[] {27}}
        };
            LoadStatus();
        UpdateTray();
    }

    private NotifyIcon TrayIcon { get; }
    private Dictionary<string, int[]> Ranks { get; }
    private bool Enabled { get; set; } = true;
    private string Status { get; set; } = null!;
    private JsonNode? DefaultPlayerJson { get; set; } = null!;
    private string StatusFile { get; } = Path.Combine(Persistence.DataDir, "config.xml");
    private bool ConnectToMuc { get; set; } = true;
    private bool InsertedFakePlayer { get; set; }
    private bool SentFakePlayerPresence { get; set; }
    private bool SentIntroductionText { get; set; }
    private string? ValorantVersion { get; set; }

    private SslStream Incoming { get; set; } = null!;
    private SslStream Outgoing { get; set; } = null!;
    private bool Connected { get; set; }
    private string LastPresence { get; set; } = null!; // we resend this if the state changes

    private string Rank { get; set; } = null!;
    private int? RankNum { get; set; } = null!;
    private int? LeaderboardNum { get; set; } = null!;
    private int? PlayerLevel { get; set; } = null!;
    private int AllyScore { get; set; } = 0;
    private int EnemyScore { get; set; } = 0;
    private int PartySize { get; set; } = 5;
    private string gameName { get; set; } = null!;

    private ToolStripMenuItem EnabledMenuItem { get; set; } = null!;
    private ToolStripMenuItem ChatStatus { get; set; } = null!;
    private ToolStripMenuItem OfflineStatus { get; set; } = null!;
    private ToolStripMenuItem AwayStatus { get; set; } = null!;
    private ToolStripMenuItem CustomStatus { get; set; } = null!;
    private ToolStripMenuItem MobileStatus { get; set; } = null!;

    private ToolStripMenuItem DefaultRank { get; set; } = null!;
    private ToolStripMenuItem IronRank { get; set; } = null!;
    private ToolStripMenuItem BronzeRank { get; set; } = null!;
    private ToolStripMenuItem SilverRank { get; set; } = null!;
    private ToolStripMenuItem GoldRank { get; set; } = null!;
    private ToolStripMenuItem PlatinumRank { get; set; } = null!;
    private ToolStripMenuItem DiamondRank { get; set; } = null!;
    private ToolStripMenuItem AscendantRank { get; set; } = null!;
    private ToolStripMenuItem ImmortalRank { get; set; } = null!;
    private ToolStripMenuItem RadiantRank { get; set; } = null!;

    private ToolStripMenuItem OneRankNum { get; set; } = null!;
    private ToolStripMenuItem TwoRankNum { get; set; } = null!;
    private ToolStripMenuItem ThreeRankNum { get; set; } = null!;


        internal event EventHandler? ConnectionErrored;

    private void UpdateTray()
    {
        var aboutMenuItem = new ToolStripMenuItem(StartupHandler.DeceiveTitle) { Enabled = false };

        EnabledMenuItem = new ToolStripMenuItem("Enabled", null, async (_, _) =>
        {
            Enabled = !Enabled;
            await UpdateStatusAsync(Enabled ? Status : "chat");
            await SendMessageFromFakePlayerAsync(Enabled ? "Deceive is now enabled." : "Deceive is now disabled.");
            UpdateTray();
        }) { Checked = Enabled };

        var mucMenuItem = new ToolStripMenuItem("Enable lobby chat", null, (_, _) =>
        {
            ConnectToMuc = !ConnectToMuc;
            UpdateTray();
        }) { Checked = ConnectToMuc };

        ToolStripMenuItem StatusItem(string item_name, string newStatus = null!)
        {
                if (newStatus is null)
                    newStatus = item_name;
                var item = new ToolStripMenuItem(item_name, null, async (_, _) =>
                {
                    await UpdateStatusAsync(Status = newStatus.ToLower());
                    Enabled = true;
                    UpdateTray();
                })
                { Checked = Status.Equals(newStatus.ToLower()) };
                return item;
        }


        ChatStatus = StatusItem("Default Online", "chat");
        OfflineStatus = StatusItem("Offline");
        AwayStatus = StatusItem("Away");
        CustomStatus = StatusItem("Custom");
        MobileStatus = StatusItem("Mobile");

        var typeMenuItem = new ToolStripMenuItem("Status Type", null, ChatStatus, OfflineStatus, AwayStatus, CustomStatus, MobileStatus);

        ToolStripMenuItem RankItem(string item_name)
        {
                var item = new ToolStripMenuItem(item_name, null, async (_, _) =>
                { 
                    Trace.WriteLine("[[CHANGING RANK TO: " + item_name);
                    LeaderboardNum = 0;
                    Rank = item_name;
                    await UpdateStatusAsync(Status);
                    await SendMessageFromFakePlayerAsync("Your rank is now set to " + Rank + " " + RankNum + " #" + LeaderboardNum);
                    Enabled = true; 
                    UpdateTray();
                })
                { Checked = Rank.Equals(item_name) };
                return item;
        }

        DefaultRank = RankItem("Default");
        IronRank = RankItem("Iron");
        BronzeRank = RankItem("Bronze");
        SilverRank = RankItem("Silver");
        GoldRank = RankItem("Gold");
        PlatinumRank = RankItem("Platinum");
        DiamondRank = RankItem("Diamond");
        AscendantRank = RankItem("Ascendant");
        ImmortalRank = RankItem("Immortal");
        RadiantRank = RankItem("Radiant");

        var rankMenuItem = new ToolStripMenuItem(
            "Rank", null, DefaultRank, IronRank, BronzeRank, SilverRank, GoldRank,
                          PlatinumRank, DiamondRank, AscendantRank, ImmortalRank, RadiantRank);

        ToolStripMenuItem RankNumItem(int item_name)
        {
            var item = new ToolStripMenuItem(item_name.ToString(), null, async (_, _) =>
            {
                RankNum = item_name;
                await UpdateStatusAsync(Status);
                Enabled = true;
                UpdateTray();
            })
            { Checked = RankNum.Equals(item_name) };
            return item;
        }

        OneRankNum = RankNumItem(1);
        TwoRankNum = RankNumItem(2);
        ThreeRankNum = RankNumItem(3);
        var rankNumItem = new ToolStripMenuItem("Rank Number", null, OneRankNum, TwoRankNum, ThreeRankNum);

        var restartWithDifferentGameItem = new ToolStripMenuItem("Restart and launch a different game", null, (_, _) =>
        {
            var result = MessageBox.Show(
                "Restart Deceive to launch a different game? This will also stop related games if they are running.",
                StartupHandler.DeceiveTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1
            );

            if (result is not DialogResult.Yes)
                return;

            Utils.KillProcesses();
            Thread.Sleep(2000);

            Persistence.SetDefaultLaunchGame(LaunchGame.Prompt);
            Process.Start(Application.ExecutablePath);
            Environment.Exit(0);
        });

        var quitMenuItem = new ToolStripMenuItem("Quit", null, (_, _) =>
        {
            var result = MessageBox.Show(
                "Are you sure you want to stop Deceive? This will also stop related games if they are running.",
                StartupHandler.DeceiveTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1
            );

            if (result is not DialogResult.Yes)
                return;

            Utils.KillProcesses();
            SaveStatus();
            Application.Exit();
        });

        TrayIcon.ContextMenuStrip = new ContextMenuStrip();

#if DEBUG
        var closeIn = new ToolStripMenuItem("Close incoming", null, (_, _) => { Incoming.Close(); });
        var closeOut = new ToolStripMenuItem("Close outgoing", null, (_, _) => { Outgoing.Close(); });
        var createFakePlayer = new ToolStripMenuItem("Resend fake player", null, async (_, _) => { await SendFakePlayerPresenceAsync(); });
        var sendTestMsg = new ToolStripMenuItem("Send message", null, async (_, _) => { await SendMessageFromFakePlayerAsync("Test"); });

        TrayIcon.ContextMenuStrip.Items.AddRange(new ToolStripItem[]
        {
            aboutMenuItem, EnabledMenuItem, typeMenuItem, rankMenuItem, rankNumItem, mucMenuItem, closeIn, closeOut, createFakePlayer, sendTestMsg, restartWithDifferentGameItem, quitMenuItem
        });
#else
        TrayIcon.ContextMenuStrip.Items.AddRange(new ToolStripItem[] { aboutMenuItem, EnabledMenuItem, typeMenuItem, mucMenuItem, restartWithDifferentGameItem, quitMenuItem });
#endif
    }

    public void StartThreads(SslStream incoming, SslStream outgoing)
    {
        Incoming = incoming;
        Outgoing = outgoing;
        Connected = true;
        InsertedFakePlayer = false;
        SentFakePlayerPresence = false;

        Task.Run(IncomingLoopAsync);
        Task.Run(OutgoingLoopAsync);
    }

    private async Task IncomingLoopAsync()
    {
        try
        {
            int byteCount;
            var bytes = new byte[8192];

            do
            {
                byteCount = await Incoming.ReadAsync(bytes, 0, bytes.Length);

                var content = Encoding.UTF8.GetString(bytes, 0, byteCount);

                // If this is possibly a presence stanza, rewrite it.
                if (content.Contains("<presence") && Enabled)
                {
                    Trace.WriteLine("<!--RC TO SERVER ORIGINAL-->" + content);
                    await PossiblyRewriteAndResendPresenceAsync(content, Status);
                }
                else if (content.Contains("41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net") && content.Contains("<body>"))
                {
                    string s = content.Split(new string[] { "body" }, StringSplitOptions.None)[1];
                    string message = s.Substring(1, s.Length - 3);
                    var args = message.Split();
                    string command = args[0];
                    if (content.ToLower().Contains("offline"))
                    {
                        if (!Enabled)
                            await SendMessageFromFakePlayerAsync("Deceive is now enabled.");
                        OfflineStatus.PerformClick();
                    }
                    else if (message.ToLower().StartsWith("mobile"))
                    {
                        if (!Enabled)
                            await SendMessageFromFakePlayerAsync("Deceive is now enabled.");
                        MobileStatus.PerformClick();
                    }
                    else if (message.ToLower().StartsWith("online"))
                    {
                        if (!Enabled)
                            await SendMessageFromFakePlayerAsync("Deceive is now enabled.");
                        ChatStatus.PerformClick();
                    }
                    else if (message.ToLower().StartsWith("enable"))
                    {
                        if (Enabled)
                            await SendMessageFromFakePlayerAsync("Deceive is already enabled.");
                        else
                            EnabledMenuItem.PerformClick();
                    }
                    else if (message.ToLower().StartsWith("disable"))
                    {
                        if (!Enabled)
                            await SendMessageFromFakePlayerAsync("Deceive is already disabled.");
                        else
                            EnabledMenuItem.PerformClick();
                    }
                    else if (message.ToLower().StartsWith("status"))
                    {
                        if (Status == "chat")
                            await SendMessageFromFakePlayerAsync("You are appearing online.");
                        else
                            await SendMessageFromFakePlayerAsync("You are appearing " + Status + ".");
                    }
                    else if (message.ToLower().StartsWith("help"))
                    {
                        await SendMessageFromFakePlayerAsync("You can send the following messages to quickly change Deceive settings: online/offline/mobile/enable/disable/status");
                    }
                    else if (message.ToLower().StartsWith("level"))
                    {
                        try
                        {
                            PlayerLevel = Int32.Parse(message.Split()[1]);
                            await UpdateStatusAsync(Status);
                            await SendMessageFromFakePlayerAsync("Level set to " + PlayerLevel);
                        }
                        catch
                        {
                            await SendMessageFromFakePlayerAsync("Invalid arguments. Syntax: level <number>");
                        }
                    }
                    else if (message.ToLower().StartsWith("leaderboard"))
                    {
                        try
                        {
                            LeaderboardNum = Int32.Parse(message.Split(' ')[1]);
                            await UpdateStatusAsync(Status);
                        }
                        catch
                        {
                            await SendMessageFromFakePlayerAsync("Invalid arguments. Syntax: leaderboard <number>");
                        }
                    }
                    else if (message.ToLower().StartsWith("gamename"))
                    {
                        try
                        {
                            gameName = message.Substring(8, message.Length-8);
                            await UpdateStatusAsync(Status);
                            await SendMessageFromFakePlayerAsync("Game name set to: " + gameName);
                        }
                        catch
                        {
                            await SendMessageFromFakePlayerAsync("Invalid arguments. Syntax: gamename <name>");
                        }
                    }
                    //Don't send anything involving our fake user to chat servers
                    Trace.WriteLine("<!--RC TO SERVER REMOVED-->" + content);
                }
                else
                {
                    await Outgoing.WriteAsync(bytes, 0, byteCount);
                    Trace.WriteLine("<!--RC TO SERVER-->" + content);
                }

                if (InsertedFakePlayer && !SentFakePlayerPresence)
                    await SendFakePlayerPresenceAsync();

                if (!SentIntroductionText)
                    await SendIntroductionTextAsync();
            } while (byteCount != 0 && Connected);
        }
        catch (Exception e)
        {
            Trace.WriteLine("Incoming errored.");
            Trace.WriteLine(e);
        }
        finally
        {
            Trace.WriteLine("Incoming closed.");
            SaveStatus();
            if (Connected)
                OnConnectionErrored();
        }
    }

    private async Task OutgoingLoopAsync()
    {
        try
        {
            int byteCount;
            var bytes = new byte[8192];

            do
            {
                byteCount = await Outgoing.ReadAsync(bytes, 0, bytes.Length);
                var content = Encoding.UTF8.GetString(bytes, 0, byteCount);

                // Insert fake player into roster
                const string roster = "<query xmlns='jabber:iq:riotgames:roster'>";
                /*
                const string idk = "<x xmlns='http://jabber.org/protocol/muc#user'>";
                bool matchingPuuid = false; 
                if (content.Contains("\'"))
                {
                    if (content.Split('\n').Length >= 4)
                        if (content.Split('\'')[1] == content.Split('\'')[3])
                        {
                            Trace.WriteLine("MATCHINGPUUID");
                            matchingPuuid = true;
                        }
                        
                }
                */
                if (!InsertedFakePlayer && content.Contains(roster))
                {
                    InsertedFakePlayer = true;
                    Trace.WriteLine("<!--SERVER TO RC ORIGINAL-->" + content);
                    content = content.Insert(content.IndexOf(roster, StringComparison.Ordinal) + roster.Length,
                        "<item jid='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net' name='&#9;Deceive Active!' subscription='both' puuid='41c322a1-b328-495b-a004-5ccd3e45eae8'>" +
                        "<group priority='9999'>Deceive</group>" +
                        "<id name='&#9;Deceive Active!' tagline=''/><lol name='&#9;Deceive Active!'/>" +
                        "</item>");
                    var contentBytes = Encoding.UTF8.GetBytes(content);
                    await Incoming.WriteAsync(contentBytes, 0, contentBytes.Length);
                    Trace.WriteLine("<!--DECEIVE TO RC-->" + content);
                }
                /*
                else if (content.Contains(idk) || matchingPuuid == true || content.StartsWith("<presence from="))
                {
                    Trace.WriteLine("<!--SERVER TO RC ORIGINAL-->" + content);
                    await PossiblyRewriteAndResendPresenceAsync(content, Status, "incoming");
                }
                */
                else
                {
                    await Incoming.WriteAsync(bytes, 0, byteCount);
                    Trace.WriteLine("<!--SERVER TO RC-->" + content);
                }
            } while (byteCount != 0 && Connected);
        }
        catch (Exception e)
        {
            Trace.WriteLine("Outgoing errored.");
            Trace.WriteLine(e);
        }
        finally
        {
            Trace.WriteLine("Outgoing closed.");
            SaveStatus();
            if (Connected)
                OnConnectionErrored();
        }
    }

    private async Task PossiblyRewriteAndResendPresenceAsync(string content, string targetStatus, string direction="outgoing")
    {
        try
        {
            LastPresence = content;
            var wrappedContent = "<xml>" + content + "</xml>";
            var xml = XDocument.Load(new StringReader(wrappedContent));

            if (xml.Root is null)
                return;
            if (xml.Root.HasElements is false)
                return;

            foreach (var presence in xml.Root.Elements())
            {
                if (presence.Name != "presence")
                    continue;
                if (presence.Attribute("to") is not null)
                {
                    if (ConnectToMuc)
                        continue;
                    presence.Remove();
                }

                if (targetStatus != "chat" || presence.Element("games")?.Element("league_of_legends")?.Element("st")?.Value != "dnd")
                {
                    if (targetStatus != "custom")
                    {
                        presence.Element("show")?.ReplaceNodes(targetStatus);
                        presence.Element("games")?.Element("league_of_legends")?.Element("st")?.ReplaceNodes(targetStatus);
                    } 
                }

                if (targetStatus == "custom" || targetStatus == "chat")
                {
                    Trace.WriteLine("CUSTOM");
                    var valorantBase64 = presence.Element("games")?.Element("valorant")?.Element("p")?.Value;
                    if (valorantBase64 is not null)
                    {
                        int? newRank = null!;
                        var valorantPresence = Encoding.UTF8.GetString(Convert.FromBase64String(valorantBase64));
                        var valorantJson = JsonSerializer.Deserialize<JsonNode>(valorantPresence);
                        if (valorantJson is not null)
                        {
                            Trace.WriteLine("!!RankBefore: " + Rank + "\naccountLevel: " + valorantJson?["accountLevel"] + "\ncompetitiveTier: " + valorantJson?["competitiveTier"] + "\nleaderboardPosition: " + valorantJson?["leaderboardPosition"]);
                            if (Rank != "Default")
                            {
                                if (Rank == "Radiant")
                                {
                                    newRank = Ranks[Rank][0];
                                }
                                else
                                {
                                    if (targetStatus == "custom" && RankNum is not null)
                                        newRank = Ranks[Rank][((int)RankNum)-1];
                                    else
                                        Trace.WriteLine("RankNum is null");
                                }
                                valorantJson["competitiveTier"] = newRank;
                            }
                            else
                            {
                                continue;
                            }

                            valorantJson["accountLevel"] = PlayerLevel;
                            //valorantJson["competitiveTier"] = newRank;
                            valorantJson["leaderboardPosition"] = LeaderboardNum;
                            if (gameName is not null)
                            {
                                valorantJson["queueId"] = gameName;
                                valorantJson["sessionLoopState"] = "INGAME";
                                valorantJson["partyOwnerSessionLoopsState"] = "INGAME";
                                valorantJson["partyOwnerMatchScoreAllyTeam"] = AllyScore;
                                valorantJson["partyOwnerMatchScoreEnemyTeam"] = EnemyScore;
                            }

                            valorantJson["playerCardId"] = "bcf9cff4-4163-a536-458d-22b8904876ad";
                            valorantJson["matchMap"] = "/Game/Maps/Canyon/Canyon";
                            valorantJson["partyId"] = "e5067d61-7d60-4e68-a0fd-71e1a6f29c58";
                            valorantJson["maxPartySize"] = 2147483647;
                            valorantJson["queueId"] = "Fuckin yo bitch";
                            valorantJson["partySize"] = 2147483647;
                            valorantJson["playerTitleId"] = "a6d9e243-4046-b025-358e-0087b4b7fcf3";
                            valorantJson["playerCardId"] = "bcf9cff4-4163-a536-458d-22b8904876ad";
                            valorantJson["preferredLevelBorderId"] = "6694d7f7-4ab9-8545-5921-35a9ea8cec24";
                            Trace.WriteLine("!!!!Rank: " + Rank +"\naccountLevel: " + valorantJson["accountLevel"] + "\ncompetitiveTier: " + valorantJson["competitiveTier"] + "\nleaderboardPosition: " + valorantJson["leaderboardPosition"]);
                            var valorantByte = Encoding.ASCII.GetBytes(JsonSerializer.Serialize(valorantJson));
                            valorantBase64 = Convert.ToBase64String(valorantByte);
                            presence.Element("games")?.Element("valorant")?.Element("p")?.ReplaceNodes(valorantBase64);
                        }
                    }
                    continue;
                }

                presence.Element("status")?.Remove();

                if (targetStatus == "mobile")
                {
                    presence.Element("games")?.Element("league_of_legends")?.Element("p")?.Remove();
                    presence.Element("games")?.Element("league_of_legends")?.Element("m")?.Remove();
                }
                else
                {
                    presence.Element("games")?.Element("league_of_legends")?.Remove();
                }

                // Remove Legends of Runeterra presence
                presence.Element("games")?.Element("bacon")?.Remove();

                // Extracts current VALORANT from the user's own presence, so that we can show a fake
                // player with the proper version and avoid "Version Mismatch" from being shown.
                //
                // This isn't technically necessary, but people keep coming in and asking whether
                // the scary red text means Deceive doesn't work, so might as well do this and
                // get a slightly better user experience.
                if (ValorantVersion is null)
                {

                    var valorantBase64 = presence.Element("games")?.Element("valorant")?.Element("p")?.Value;
                    if (valorantBase64 is not null)
                    {
                        var valorantPresence = Encoding.UTF8.GetString(Convert.FromBase64String(valorantBase64));
                        var valorantJson = JsonSerializer.Deserialize<JsonNode>(valorantPresence);
                        ValorantVersion = valorantJson?["partyClientVersion"]?.GetValue<string>();
                        Trace.WriteLine("Found VALORANT version: " + ValorantVersion);

                        if (DefaultPlayerJson is null)
                            DefaultPlayerJson = valorantJson;
                            SetProfileDefault();
                            // only resend
                        if (InsertedFakePlayer && ValorantVersion is not null)
                            await SendFakePlayerPresenceAsync();
                    }
                }

                // Remove VALORANT presence
                presence.Element("games")?.Element("valorant")?.Remove();
            }

            var sb = new StringBuilder();
            var xws = new XmlWriterSettings { OmitXmlDeclaration = true, Encoding = Encoding.UTF8, ConformanceLevel = ConformanceLevel.Fragment, Async = true };
            using (var xw = XmlWriter.Create(sb, xws))
            { 
                foreach (var xElement in xml.Root.Elements())
                    xElement.WriteTo(xw);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            if (direction == "incoming")
            {
                await Incoming.WriteAsync(bytes, 0, bytes.Length);
                Trace.WriteLine("<!--DECEIVE TO RC-->" + sb);
            }  
            else if (direction == "outgoing")
            {
                await Outgoing.WriteAsync(bytes, 0, bytes.Length);
                Trace.WriteLine("<!--DECEIVE TO SERVER-->" + sb);
            }   
        }
        catch (Exception e)
        {
            Trace.WriteLine(e);
            Trace.WriteLine("Error rewriting presence.");
        }
    }
    private void SetProfileDefault()
    {
        foreach (KeyValuePair<string, int[]> item in Ranks)
        {
            int[] i = item.Value;
            int index = 0;
            foreach(int j in i)
            {
                index += 1;
                if (j == (int)DefaultPlayerJson["competitiveTier"])
                {
                    Rank = item.Key;
                    Trace.WriteLine("DEFAULTRANK: " + item.Key);
                    RankNum = index;
                }
            }
        }
        gameName = null!;
        PlayerLevel = (int)DefaultPlayerJson["accountLevel"];
        LeaderboardNum = (int)DefaultPlayerJson["leaderboardPosition"];
    }
    private async Task SendFakePlayerPresenceAsync()
    {
        SentFakePlayerPresence = true;
        // VALORANT requires a recent version to not display "Version Mismatch"
        var valorantPresence = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{{\"isValid\":true,\"partyId\":\"00000000-0000-0000-0000-000000000000\",\"partyClientVersion\":\"{ValorantVersion ?? "unknown"}\",\"competitiveTier\":2,\"leaderboardPosition\":1}}")
        );

        var randomStanzaId = Guid.NewGuid();
        var unixTimeMilliseconds = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        var presenceMessage =
            $"<presence from='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net/RC-Deceive' id='b-{randomStanzaId}'>" +
            "<games>" +
            $"<keystone><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.p>keystone</s.p></keystone>" +
            $"<league_of_legends><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.p>league_of_legends</s.p><p>{{&quot;pty&quot;:true}}</p></league_of_legends>" + // No Region s.r keeps it in the main "League" category rather than "Other Servers" in every region with "Group Games & Servers" active
            $"<valorant><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.p>valorant</s.p><p>{valorantPresence}</p></valorant>" +
            $"<bacon><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.l>bacon_availability_online</s.l><s.p>bacon</s.p></bacon>" +
            "</games>" +
            "<show>chat</show>" +
            "</presence>";

        var bytes = Encoding.UTF8.GetBytes(presenceMessage);
        await Incoming.WriteAsync(bytes, 0, bytes.Length);
        Trace.WriteLine("<!--DECEIVE TO RC-->" + presenceMessage);
    }

    private async Task SendIntroductionTextAsync()
    {
        if (!InsertedFakePlayer)
            return;
        SentIntroductionText = true;
        await SendMessageFromFakePlayerAsync("Welcome! Deceive is running and you are currently appearing " + Status +
                                             ". Despite what the game client may indicate, you are appearing offline to your friends unless you manually disable Deceive.");
        await Task.Delay(200);
        await SendMessageFromFakePlayerAsync(
            "If you want to invite others while being offline, you may need to disable Deceive for them to accept. You can enable Deceive again as soon as they are in your lobby.");
        await Task.Delay(200);
        await SendMessageFromFakePlayerAsync("To enable or disable Deceive, or to configure other settings, find Deceive in your tray icons.");
        await Task.Delay(200);
        await SendMessageFromFakePlayerAsync("Have fun!");
    }

    private async Task SendMessageFromFakePlayerAsync(string message)
    {
        var stamp = DateTime.UtcNow.AddSeconds(1).ToString("yyyy-MM-dd HH:mm:ss.fff");

        var chatMessage =
            $"<message from='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net/RC-Deceive' stamp='{stamp}' id='fake-{stamp}' type='chat'><body>{message}</body></message>";

        var bytes = Encoding.UTF8.GetBytes(chatMessage);
        await Incoming.WriteAsync(bytes, 0, bytes.Length);
        Trace.WriteLine("<!--DECEIVE TO RC-->" + chatMessage);
    }

    private async Task UpdateStatusAsync(string newStatus)
    {
        if (string.IsNullOrEmpty(LastPresence))
            return;

        await PossiblyRewriteAndResendPresenceAsync(LastPresence, newStatus);

        if (newStatus == "offline")
            await SendMessageFromFakePlayerAsync("You are now appearing offline.");
        else if (newStatus == "custom" && gameName is not null)
            await SendMessageFromFakePlayerAsync("You are now " + gameName + ". Your rank is" + Rank + " " + RankNum + " "+ LeaderboardNum + " Level " + PlayerLevel);
        else
            await SendMessageFromFakePlayerAsync("You are now " + newStatus + ". Your rank is" + Rank + " " + RankNum + " " + LeaderboardNum + " Level " + PlayerLevel);
    }

    private void LoadStatus()
    {
        if (File.Exists(StatusFile))
            Status = File.ReadAllText(StatusFile) == "mobile" ? "mobile" : "offline";
        else
            Status = "offline";
    }

    private void SaveStatus() => File.WriteAllText(StatusFile, Status);

    private void OnConnectionErrored()
    {
        Connected = false;
        ConnectionErrored?.Invoke(this, EventArgs.Empty);
    }
}
}
