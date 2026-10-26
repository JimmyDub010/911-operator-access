using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using Call911.GUI;
using Call911.GameLogic;
using Call911.Managers;
using Call911.Police;
using Call911.Report;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OperatorAccessibility
{
    [BepInPlugin("com.jwood.911operator.accessibility", "911 Operator Accessibility Prototype", "0.1.0")]
    public sealed class AccessibilityPlugin : BaseUnityPlugin
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        [DllImport("nvdaControllerClient.dll")]
        private static extern int nvdaController_testIfRunning();

        [DllImport("nvdaControllerClient.dll")]
        private static extern int nvdaController_cancelSpeech();

        [DllImport("nvdaControllerClient.dll", CharSet = CharSet.Unicode)]
        private static extern int nvdaController_speakText(string text);

        [DllImport("nvdaControllerClient.dll", CharSet = CharSet.Unicode)]
        private static extern int nvdaController_brailleMessage(string text);

        private readonly List<Button> menuButtons = new List<Button>();
        private UdpClient speechClient;
        private Process speechProcess;
        private bool nvdaClientLoaded;
        private int menuIndex = -1;
        private int unitIndex = -1;
        private int incidentIndex = -1;
        private int summaryReportIndex = -1;
        private string lastSelectionName = String.Empty;
        private DateTime nextMenuRefreshUtc;
        private DateTime announceAtUtc;
        private readonly Dictionary<int, string> knownUnitStates = new Dictionary<int, string>();
        private readonly Dictionary<int, ReportInstance> knownReports = new Dictionary<int, ReportInstance>();
        private int monitoredManagerId;
        private int announcedCallId;
        private string lastConversationText = String.Empty;
        private string pendingConversationText = String.Empty;
        private string lastChoiceSignature = String.Empty;
        private DateTime conversationTextChangedUtc = DateTime.MinValue;
        private readonly HashSet<string> announcedDialogueSegments = new HashSet<string>();
        private int monitoredConversationId;
        private int callSpeechMode = -1;
        private DateTime nextCallRecoveryUtc = DateTime.MinValue;
        private static readonly FieldInfo ConversationWasClickedField = typeof(ConversationElement).GetField("wasClicked", BindingFlags.Instance | BindingFlags.NonPublic);

        private sealed class UnitDistance
        {
            public int UnitIndex;
            public double Miles;
            public string Department;
        }

        private void Awake()
        {
            try
            {
                string pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string nvdaPath = Path.Combine(pluginDirectory, "nvdaControllerClient.dll");
                if (File.Exists(nvdaPath))
                    nvdaClientLoaded = LoadLibrary(nvdaPath) != IntPtr.Zero;
                string helperPath = Path.Combine(pluginDirectory, "OperatorSpeech.exe");
                if ((!nvdaClientLoaded || !IsNvdaRunning()) && File.Exists(helperPath))
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo(helperPath);
                    startInfo.UseShellExecute = false;
                    startInfo.CreateNoWindow = true;
                    startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    speechProcess = Process.Start(startInfo);
                }
                speechClient = new UdpClient();
                speechClient.Connect(IPAddress.Loopback, 49091);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Speech helper initialization failed: " + ex.Message);
            }

            announceAtUtc = DateTime.UtcNow.AddSeconds(2);
            nextMenuRefreshUtc = DateTime.UtcNow;
            Logger.LogInfo("Accessibility prototype loaded.");
        }

        private void OnDestroy()
        {
            try
            {
                SendToSpeechHelper("__EXIT__");
                if (speechClient != null)
                    speechClient.Close();
            }
            catch
            {
            }
        }

        private void Update()
        {
            DateTime now = DateTime.UtcNow;
            if (announceAtUtc != DateTime.MinValue && now >= announceAtUtc)
            {
                announceAtUtc = DateTime.MinValue;
                Speak("Accessibility prototype loaded. Press F1 to read the current menu item. Use up and down arrows and Enter.");
            }

            if (now >= nextMenuRefreshUtc)
            {
                nextMenuRefreshUtc = now.AddMilliseconds(500);
                RefreshMenuButtons();
                AnnounceExternalSelectionChange();
                MonitorGameEvents(now);
            }

            if (Input.GetKeyDown(KeyCode.F1))
                SpeakCurrentControl();
            if (Input.GetKeyDown(KeyCode.F2))
                SpeakUnit(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1);
            if (Input.GetKeyDown(KeyCode.F3))
                SpeakIncident(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1);
            if (Input.GetKeyDown(KeyCode.F4))
                DisableTutorial();
            if (Input.GetKeyDown(KeyCode.F5))
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                    CancelCurrentAssignment();
                else
                    DispatchCurrentUnit();
            }
            if (Input.GetKeyDown(KeyCode.F6))
                AnswerIncomingCall();
            if (Input.GetKeyDown(KeyCode.F7))
                RecoverCallChoices(now);
            if (Input.GetKeyDown(KeyCode.F8))
                CycleCallSpeechMode();
            if (Input.GetKeyDown(KeyCode.F9))
                SelectNearestAppropriateUnit(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1);
            if (Input.GetKeyDown(KeyCode.F10))
                ChangeIncidentPressure(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1);
            if (Input.GetKeyDown(KeyCode.F11))
            {
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                    SpeakDutySummaryTotals();
                else
                    SpeakDutySummaryReport(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1);
            }

            if (IsNavigationContext())
            {
                if (Input.GetKeyDown(KeyCode.DownArrow))
                    MoveMenu(1);
                else if (Input.GetKeyDown(KeyCode.UpArrow))
                    MoveMenu(-1);
                else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                    ActivateCurrentMenuItem();
            }
        }

        private bool IsNavigationContext()
        {
            if (HasActiveTalkButtons() || GetModalRoot() != null || UnityEngine.Object.FindObjectOfType<GUIMenuManager>() != null)
                return true;
            GameManager manager = UnityEngine.Object.FindObjectOfType<GameManager>();
            return manager != null && !manager.dutyMode;
        }

        private Transform GetModalRoot()
        {
            TutorialManager tutorial = UnityEngine.Object.FindObjectOfType<TutorialManager>();
            if (tutorial != null && tutorial.gameObject.activeInHierarchy && tutorial.state.ToString() != "OFF")
                return tutorial.transform;

            PopUpManager popUpManager = UnityEngine.Object.FindObjectOfType<PopUpManager>();
            if (popUpManager != null && popUpManager.popUp != null && popUpManager.popUp.activeInHierarchy)
                return popUpManager.popUp.transform;

            GameManager manager = UnityEngine.Object.FindObjectOfType<GameManager>();
            if (manager != null && manager.DisplayedReport != null && manager.DisplayedReport.reportWindow != null
                && manager.DisplayedReport.reportWindow.gameObject.activeInHierarchy)
                return manager.DisplayedReport.reportWindow.transform;
            return null;
        }

        private void RefreshMenuButtons()
        {
            menuButtons.Clear();
            TalkButton[] talkButtons = Resources.FindObjectsOfTypeAll<TalkButton>();
            for (int i = 0; i < talkButtons.Length; i++)
            {
                TalkButton talkButton = talkButtons[i];
                if (talkButton == null || !talkButton.gameObject.activeInHierarchy)
                    continue;
                ConversationElement talkElement = talkButton.getElement();
                if (IsConversationElementUsed(talkElement))
                    continue;
                AddActiveButton(talkButton.GetComponent<Button>());
            }
            if (menuButtons.Count > 0)
            {
                if (menuIndex < 0 || menuIndex >= menuButtons.Count)
                    menuIndex = 0;
                return;
            }
            TipManager tipManager = UnityEngine.Object.FindObjectOfType<TipManager>();
            if (tipManager != null && tipManager.tip != null && tipManager.tip.activeInHierarchy)
            {
                AddActiveButton(tipManager.prevTipButton);
                AddActiveButton(tipManager.closeTipButton);
                AddActiveButton(tipManager.nextTipButton);
                if (menuButtons.Count == 0)
                    menuIndex = -1;
                else if (menuIndex < 0 || menuIndex >= menuButtons.Count)
                    menuIndex = 0;
                return;
            }
            Transform modalRoot = GetModalRoot();
            Button[] buttons = Resources.FindObjectsOfTypeAll<Button>();
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button == null || !button.gameObject.activeInHierarchy || !button.interactable)
                    continue;
                if (!button.gameObject.scene.IsValid())
                    continue;
                if (modalRoot != null && button.transform != modalRoot && !button.transform.IsChildOf(modalRoot))
                    continue;
                menuButtons.Add(button);
            }

            menuButtons.Sort(delegate(Button a, Button b)
            {
                return String.Compare(GetHierarchyPath(a.transform), GetHierarchyPath(b.transform), StringComparison.Ordinal);
            });

            if (menuButtons.Count == 0)
                menuIndex = -1;
            else if (menuIndex < 0 || menuIndex >= menuButtons.Count)
                menuIndex = 0;
        }

        private void AddActiveButton(Button button)
        {
            if (button != null && button.gameObject.activeInHierarchy && button.interactable)
                menuButtons.Add(button);
        }

        private bool HasActiveTalkButtons()
        {
            TalkButton[] talkButtons = Resources.FindObjectsOfTypeAll<TalkButton>();
            for (int i = 0; i < talkButtons.Length; i++)
            {
                if (talkButtons[i] != null && talkButtons[i].gameObject.activeInHierarchy)
                    return true;
            }
            return false;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            string path = transform.GetSiblingIndex().ToString("D4") + ":" + transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.GetSiblingIndex().ToString("D4") + ":" + transform.name + "/" + path;
            }
            return path;
        }

        private void MoveMenu(int direction)
        {
            RefreshMenuButtons();
            if (menuButtons.Count == 0)
            {
                Speak("No active menu buttons found.");
                return;
            }

            menuIndex = (menuIndex + direction + menuButtons.Count) % menuButtons.Count;
            Button button = menuButtons[menuIndex];
            button.Select();
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(button.gameObject);
            lastSelectionName = button.gameObject.GetInstanceID().ToString();
            Speak(DescribeButton(button));
        }

        private void ActivateCurrentMenuItem()
        {
            RefreshMenuButtons();
            Button button = GetCurrentButton();
            if (button == null)
            {
                Speak("No menu item selected.");
                return;
            }

            ManagementGUI management = UnityEngine.Object.FindObjectOfType<ManagementGUI>();
            GameManager manager = UnityEngine.Object.FindObjectOfType<GameManager>();
            if (management != null && manager != null && button == management.startDeployment)
            {
                Speak("Starting deployment.");
                manager.startDeployment();
                return;
            }
            if (manager != null && button == manager.finishDeployment)
            {
                Speak("Starting duty.");
                manager.startDuty();
                return;
            }
            Speak("Activating " + DescribeButton(button));
            // Do not invoke ordinary buttons here. Unity's EventSystem processes
            // this same Enter press after Update. Invoking manually as well would
            // activate conversation choices twice and corrupt their back history.
        }

        private void SpeakCurrentControl()
        {
            RefreshMenuButtons();
            Button button = GetCurrentButton();
            TipManager tipManager = UnityEngine.Object.FindObjectOfType<TipManager>();
            if (tipManager != null && tipManager.tip != null && tipManager.tip.activeInHierarchy)
            {
                StringBuilder tipText = new StringBuilder();
                if (tipManager.tipTitle != null && !String.IsNullOrEmpty(tipManager.tipTitle.text))
                    tipText.Append(tipManager.tipTitle.text.Trim()).Append(". ");
                if (tipManager.tipDesc != null && !String.IsNullOrEmpty(tipManager.tipDesc.text))
                    tipText.Append(tipManager.tipDesc.text.Trim()).Append(". ");
                if (button != null)
                    tipText.Append(DescribeButton(button));
                Speak(tipText.ToString());
                return;
            }
            if (button != null)
            {
                Speak(DescribeButton(button));
                return;
            }

            GameObject selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            Speak(selected == null ? "No Unity user interface control is selected." : DescribeObject(selected));
        }

        private Button GetCurrentButton()
        {
            GameObject selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            if (selected != null)
            {
                Button selectedButton = selected.GetComponent<Button>();
                if (selectedButton != null && selectedButton.gameObject.activeInHierarchy && selectedButton.interactable)
                {
                    int selectedIndex = menuButtons.IndexOf(selectedButton);
                    if (selectedIndex >= 0)
                        menuIndex = selectedIndex;
                    return selectedButton;
                }
            }
            return menuIndex >= 0 && menuIndex < menuButtons.Count ? menuButtons[menuIndex] : null;
        }

        private void AnnounceExternalSelectionChange()
        {
            if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null)
                return;
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            string selectionName = selected.GetInstanceID().ToString();
            if (selectionName == lastSelectionName)
                return;
            lastSelectionName = selectionName;
            Button button = selected.GetComponent<Button>();
            if (button != null)
                Speak(DescribeButton(button));
        }

        private string DescribeButton(Button button)
        {
            string description = DescribeObject(button.gameObject) + ", button";
            int difficulty = GetDifficultyButtonNumber(button);
            if (difficulty == 1)
                return description + ". Easy difficulty. Fewer incidents and weaker criminals. Recommended for learning the accessible controls.";
            if (difficulty == 2)
                return description + ". Normal difficulty. Regular incident frequency and higher penalties for failed incidents.";
            if (difficulty == 3)
                return description + ". Hard difficulty. More incidents and fights, stronger criminals, and higher failure penalties.";
            if (difficulty == 4)
                return description + ". Expert difficulty. Even more incidents, stronger criminals, higher penalties, and no in-game pause.";
            return description;
        }

        private static int GetDifficultyButtonNumber(Button button)
        {
            GUIMenuManager menu = UnityEngine.Object.FindObjectOfType<GUIMenuManager>();
            if (menu == null || menu.difficultyWindow == null || !menu.difficultyWindow.activeInHierarchy)
                return 0;
            FieldInfo field = typeof(GUIMenuManager).GetField("difficultyButtons", BindingFlags.Instance | BindingFlags.NonPublic);
            Button[] buttons = System.Object.ReferenceEquals(field, null) ? null : field.GetValue(menu) as Button[];
            if (buttons == null)
                return 0;
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == button)
                    return i + 1;
            }
            return 0;
        }

        private static string DescribeObject(GameObject gameObject)
        {
            Text[] texts = gameObject.GetComponentsInChildren<Text>(true);
            StringBuilder description = new StringBuilder();
            for (int i = 0; i < texts.Length; i++)
            {
                string value = texts[i].text == null ? String.Empty : texts[i].text.Trim();
                if (value.Length == 0)
                    continue;
                if (description.Length > 0)
                    description.Append(". ");
                description.Append(value);
            }
            return description.Length > 0 ? description.ToString() : gameObject.name;
        }

        private void SpeakUnit(int direction)
        {
            GameManager manager = UnityEngine.Object.FindObjectOfType<GameManager>();
            if (manager == null || manager.policeList == null)
            {
                Speak("No active duty units are available in the current screen.");
                return;
            }

            if (manager.policeList.Count == 0)
            {
                Speak("No active units.");
                return;
            }
            unitIndex = (unitIndex + direction + manager.policeList.Count) % manager.policeList.Count;
            PoliceBase unit = manager.policeList[unitIndex];
            if (unit == null || unit.squad == null || unit.squad.vehicles.Count == 0)
            {
                Speak("Unit " + (unitIndex + 1) + " data unavailable.");
                return;
            }
            StringBuilder text = new StringBuilder();
            text.Append("Unit ").Append(unitIndex + 1).Append(" of ").Append(manager.policeList.Count).Append(". ");
            string code = String.IsNullOrEmpty(unit.squad.code) ? "unit " + unit.squad.squadNo : unit.squad.code;
            text.Append(code).Append(", ").Append(unit.squad.vehicles[0].ofType.type).Append(", ")
                .Append(unit.behaviour == null ? "unknown status" : unit.behaviour.getStateName());
            string department = unit.squad.vehicles[0].ofType.type;
            text.Append(", ").Append(unit.squad.vehicles[0].ofType.name);
            if (department == "police")
            {
                int totalSeats = unit.squad.getMaxCriminals();
                int freeSeats = Math.Max(0, totalSeats - unit.criminalsInCar.Count);
                text.Append(", ").Append(freeSeats).Append(" of ").Append(totalSeats).Append(" prisoner transport seats free");
            }
            else if (department == "medical")
            {
                int totalPlaces = unit.squad.getMaxInjuried();
                int freePlaces = Math.Max(0, totalPlaces - unit.injuriedInCar.Count);
                text.Append(", ").Append(freePlaces).Append(" of ").Append(totalPlaces).Append(" patient transport places free");
            }
            if (!String.IsNullOrEmpty(unit.atPlaceName))
                text.Append(", at ").Append(unit.atPlaceName);
            if (unit.behaviour != null && unit.behaviour.targetReport != null && unit.behaviour.targetReport.reportInstance != null)
            {
                string targetName = unit.behaviour.targetReport.reportInstance.ofType == null
                    ? "an incident"
                    : unit.behaviour.targetReport.reportInstance.ofType.name;
                text.Append(", assigned to ").Append(targetName)
                    .Append(" at ").Append(unit.behaviour.targetReport.reportInstance.address);
            }
            Speak(text.ToString());
        }

        private void SpeakIncident(int direction)
        {
            GameManager manager = UnityEngine.Object.FindObjectOfType<GameManager>();
            if (manager == null || manager.reportList == null)
            {
                Speak("No active incidents are available in the current screen.");
                return;
            }

            if (manager.reportList.Count == 0)
            {
                Speak("No active incidents.");
                return;
            }
            incidentIndex = (incidentIndex + direction + manager.reportList.Count) % manager.reportList.Count;
            ReportBase report = manager.reportList[incidentIndex];
            if (report == null || report.reportInstance == null)
            {
                Speak("Incident " + (incidentIndex + 1) + " data unavailable.");
                return;
            }
            StringBuilder text = new StringBuilder();
            string incidentName = report.reportInstance.ofType == null ? "unknown incident" : report.reportInstance.ofType.name;
            text.Append("Incident ").Append(incidentIndex + 1).Append(" of ").Append(manager.reportList.Count)
                .Append(". ").Append(incidentName).Append(", at ").Append(report.reportInstance.address)
                .Append(", ").Append((int)report.reportInstance.timeLeftToReact).Append(" seconds remaining");
            text.Append(", ").Append(DescribeIncidentState(report));
            int responding = report.assignedTeams == null ? 0 : report.assignedTeams.Count;
            int onSite = report.reportInstance.eventScene == null ? 0 : report.reportInstance.eventScene.getSquads().Count;
            if (responding > 0)
                text.Append(", ").Append(responding).Append(responding == 1 ? " unit responding" : " units responding");
            if (onSite > 0)
                text.Append(", ").Append(onSite).Append(onSite == 1 ? " unit on site" : " units on site");
            Speak(text.ToString());
        }

        private static string DescribeIncidentState(ReportBase report)
        {
            if (report == null || report.reportInstance == null)
                return "unknown state";
            int responding = report.assignedTeams == null ? 0 : report.assignedTeams.Count;
            int onSite = report.reportInstance.eventScene == null ? 0 : report.reportInstance.eventScene.getSquads().Count;
            string state = report.behaviour == null ? String.Empty : report.behaviour.getStateName();
            if (onSite > 0)
            {
                if (state == "STATE_FIGHTING")
                    return "units fighting on site";
                return "units working on site";
            }
            if (responding > 0)
                return "units responding";
            if (state == "STATE_ONRUN")
                return "moving incident";
            if (state == "STATE_RESOLVED")
                return "resolved";
            if (state == "STATE_RESOLVING")
                return "being resolved";
            if (state == "STATE_FIGHTING")
                return "fight in progress";
            if (state == "STATE_OFF" || String.IsNullOrEmpty(state))
                return "waiting for response";
            return state.Replace("STATE_", String.Empty).ToLowerInvariant();
        }

        private void SelectNearestAppropriateUnit(int direction)
        {
            GameManager manager = UnityEngine.Object.FindObjectOfType<GameManager>();
            if (manager == null || !manager.dutyMode)
            {
                Speak("Nearest unit selection is available only after duty has started.");
                return;
            }
            if (manager.reportList == null || manager.reportList.Count == 0 || incidentIndex < 0 || incidentIndex >= manager.reportList.Count)
            {
                Speak("Browse an incident with F3 first.");
                return;
            }
            if (manager.policeList == null || manager.policeList.Count == 0)
            {
                Speak("No active units.");
                return;
            }

            ReportBase reportBase = manager.reportList[incidentIndex];
            ReportInstance report = reportBase == null ? null : reportBase.reportInstance;
            if (report == null || report.eventScene == null)
            {
                Speak("That incident does not yet contain enough information to identify an appropriate unit.");
                return;
            }

            bool needsPolice = false;
            bool needsMedical = false;
            bool needsFire = false;
            bool policeTransportNeeded = false;
            foreach (EventElement element in report.eventScene.getElements().Values)
            {
                if (element == null || !element.isActive)
                    continue;
                EventCriminal criminal = element as EventCriminal;
                if (criminal != null && criminal.resolveType == EventCriminal.CRIMINAL_TYPE.TYPE_PRISON && criminal.isResolved()
                    && criminal.state != EventCriminal.CRIMINAL_STATES.STATE_RESOLVED)
                {
                    needsPolice = true;
                    policeTransportNeeded = true;
                }
                bool unresolved;
                try
                {
                    unresolved = !element.isResolved();
                }
                catch
                {
                    continue;
                }
                if (!unresolved)
                    continue;
                bool isDead = element.GetType().Name == "EventDead";
                if (element is EventCriminal || isDead)
                    needsPolice = true;
                if (element is Injuried || isDead)
                    needsMedical = true;
                if (element is FireJob)
                    needsFire = true;
            }

            if (!needsPolice && !needsMedical && !needsFire)
            {
                Speak("No unresolved police, medical, or fire requirement was identified for this incident.");
                return;
            }

            List<UnitDistance> candidates = new List<UnitDistance>();
            for (int i = 0; i < manager.policeList.Count; i++)
            {
                PoliceBase unit = manager.policeList[i];
                if (unit == null || unit.behaviour == null || unit.squad == null || unit.squad.vehicles == null || unit.squad.vehicles.Count == 0)
                    continue;
                if (unit.behaviour.getStateName() != "PATROL")
                    continue;
                string department = unit.squad.vehicles[0].ofType.type;
                bool appropriate = (department == "police" && needsPolice)
                    || (department == "medical" && needsMedical)
                    || (department == "fire" && needsFire);
                if (!appropriate)
                    continue;
                if (department == "police" && policeTransportNeeded && !unit.hasFreePlace())
                    continue;
                Vector2 unitCoordinates = manager.VectToLatLon(unit.transform.position.x, unit.transform.position.y);
                UnitDistance candidate = new UnitDistance();
                candidate.UnitIndex = i;
                candidate.Department = department;
                candidate.Miles = HaversineMiles(unitCoordinates.y, unitCoordinates.x, report.lat, report.lon);
                candidates.Add(candidate);
            }

            candidates.Sort(delegate(UnitDistance a, UnitDistance b)
            {
                return a.Miles.CompareTo(b.Miles);
            });
            if (candidates.Count == 0)
            {
                Speak(policeTransportNeeded
                    ? "Police transport is needed, but no police unit with a free prisoner seat is currently on patrol."
                    : "No available appropriate units are currently on patrol for this incident.");
                return;
            }

            int candidateIndex = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].UnitIndex == unitIndex)
                {
                    candidateIndex = i;
                    break;
                }
            }
            if (candidateIndex < 0)
                candidateIndex = direction > 0 ? 0 : candidates.Count - 1;
            else
                candidateIndex = (candidateIndex + direction + candidates.Count) % candidates.Count;

            UnitDistance selected = candidates[candidateIndex];
            unitIndex = selected.UnitIndex;
            PoliceBase selectedUnit = manager.policeList[unitIndex];
            string unitName = String.IsNullOrEmpty(selectedUnit.squad.code) ? "unit " + selectedUnit.squad.squadNo : selectedUnit.squad.code;
            string incidentName = report.ofType == null ? "Incident" : report.ofType.name;
            Speak(incidentName + ", at " + report.address + ". Candidate " + (candidateIndex + 1) + " of " + candidates.Count
                + ": " + unitName + ", " + selected.Department + ", " + selected.Miles.ToString("0.0")
                + " miles away" + (policeTransportNeeded ? ", with a free prisoner transport seat" : String.Empty)
                + ". Press F5 to dispatch.");
        }

        private static double HaversineMiles(double latitude1, double longitude1, double latitude2, double longitude2)
        {
            const double EarthRadiusMiles = 3958.7613;
            double radians = Math.PI / 180.0;
            double latitudeDelta = (latitude2 - latitude1) * radians;
            double longitudeDelta = (longitude2 - longitude1) * radians;
            double a = Math.Sin(latitudeDelta / 2.0) * Math.Sin(latitudeDelta / 2.0)
                + Math.Cos(latitude1 * radians) * Math.Cos(latitude2 * radians)
                * Math.Sin(longitudeDelta / 2.0) * Math.Sin(longitudeDelta / 2.0);
            return EarthRadiusMiles * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        }

        private void ChangeIncidentPressure(int direction)
        {
            GameManager manager = UnityEngine.Object.FindObjectOfType<GameManager>();
            if (manager == null || manager.levelData == null)
            {
                Speak("Incident pressure can be changed after a city has loaded.");
                return;
            }
            int current = (int)Math.Round(manager.levelData.difficulty);
            if (current < 1 || current > 4)
                current = 2;
            int next = ((current - 1 + direction + 4) % 4) + 1;
            manager.levelData.difficulty = next;
            Preferences.difficulty = next;
            if (next == 1)
                Speak("Incident pressure set to Easy. Fewer incidents and weaker criminals.");
            else if (next == 2)
                Speak("Incident pressure set to Normal. Regular incident frequency.");
            else if (next == 3)
                Speak("Incident pressure set to Hard. More incidents and fights, with stronger criminals.");
            else
                Speak("Incident pressure set to Expert. Even more incidents, stronger criminals, and no in-game pause.");
        }

        private void SpeakDutySummaryReport(int direction)
        {
            DutySummary summary = UnityEngine.Object.FindObjectOfType<DutySummary>();
            GameManager manager = UnityEngine.Object.FindObjectOfType<GameManager>();
            if (summary == null || !summary.gameObject.activeInHierarchy || manager == null || manager.levelData == null
                || manager.levelData.statistics == null)
            {
                Speak("Individual duty reports are available on the after-duty summary screen.");
                return;
            }
            List<ReportInstance> reports = manager.levelData.statistics.removedReports;
            if (reports == null || reports.Count == 0)
            {
                Speak("No completed incident reports are available for this duty.");
                return;
            }
            summaryReportIndex = (summaryReportIndex + direction + reports.Count) % reports.Count;
            ReportInstance report = reports[summaryReportIndex];
            string name = report.ofType == null ? "Incident" : report.ofType.name;
            string details = NormalizeUiText(report.afterActionReport);
            if (String.IsNullOrEmpty(details))
                details = "No detailed outcome was recorded.";
            string reputation = report.finalOpinion > 0f ? "+" + report.finalOpinion : report.finalOpinion.ToString();
            Speak("Report " + (summaryReportIndex + 1) + " of " + reports.Count + ". " + name + " at " + report.address
                + ". Outcome " + report.wayResolved.ToString().ToLowerInvariant() + ". Reputation " + reputation + ". " + details);
        }

        private void SpeakDutySummaryTotals()
        {
            DutySummary summary = UnityEngine.Object.FindObjectOfType<DutySummary>();
            if (summary == null || !summary.gameObject.activeInHierarchy)
            {
                Speak("Duty totals are available on the after-duty summary screen.");
                return;
            }
            StringBuilder text = new StringBuilder("Duty summary. ");
            AppendSummaryText(text, summary.lastResolvedReports);
            AppendSummaryText(text, summary.lastReportsSummary);
            AppendSummaryText(text, summary.lastEconomySummary);
            Speak(text.ToString());
        }

        private static void AppendSummaryText(StringBuilder destination, Text source)
        {
            if (source == null)
                return;
            string value = NormalizeUiText(source.text);
            if (!String.IsNullOrEmpty(value))
                destination.Append(value).Append(". ");
        }

        private void DispatchCurrentUnit()
        {
            GameManager manager = UnityEngine.Object.FindObjectOfType<GameManager>();
            if (manager == null || !manager.dutyMode)
            {
                Speak("Dispatch is available only after duty has started.");
                return;
            }
            if (manager.policeList == null || manager.policeList.Count == 0 || unitIndex < 0 || unitIndex >= manager.policeList.Count)
            {
                Speak("Browse a unit with F2 first.");
                return;
            }
            if (manager.reportList == null || manager.reportList.Count == 0 || incidentIndex < 0 || incidentIndex >= manager.reportList.Count)
            {
                Speak("Browse an incident with F3 first.");
                return;
            }
            PoliceBase unit = manager.policeList[unitIndex];
            ReportBase report = manager.reportList[incidentIndex];
            if (unit == null || unit.behaviour == null || report == null || report.reportInstance == null)
            {
                Speak("That unit or incident is no longer available.");
                return;
            }
            if (unit.behaviour.getStateName() == "OFF")
            {
                Speak("That unit is off duty and cannot be dispatched.");
                return;
            }
            string unitName = String.IsNullOrEmpty(unit.squad.code) ? "unit " + unit.squad.squadNo : unit.squad.code;
            string incidentName = report.reportInstance.ofType == null ? "incident" : report.reportInstance.ofType.name;
            try
            {
                unit.GoToReport(report);
                Speak("Dispatched " + unitName + " to " + incidentName + " at " + report.reportInstance.address + ".");
            }
            catch (Exception ex)
            {
                Logger.LogError("Dispatch failed: " + ex);
                Speak("Dispatch failed. See the accessibility log for details.");
            }
        }

        private void CancelCurrentAssignment()
        {
            GameManager manager = UnityEngine.Object.FindObjectOfType<GameManager>();
            if (manager == null || manager.policeList == null || manager.policeList.Count == 0 || unitIndex < 0 || unitIndex >= manager.policeList.Count)
            {
                Speak("Browse a unit with F2 first.");
                return;
            }
            PoliceBase unit = manager.policeList[unitIndex];
            if (unit == null || unit.behaviour == null || unit.behaviour.targetReport == null)
            {
                Speak("That unit has no current incident assignment.");
                return;
            }
            string unitName = String.IsNullOrEmpty(unit.squad.code) ? "unit " + unit.squad.squadNo : unit.squad.code;
            try
            {
                unit.behaviour.getState().goToPlace(unit.transform.position);
                if (unit.behaviour.targetReport == null)
                    Speak(unitName + " assignment canceled. Returning to patrol.");
                else
                    Speak(unitName + " could not leave the current incident.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Cancel assignment failed: " + ex);
                Speak("Unable to cancel that assignment. See the accessibility log for details.");
            }
        }

        private void AnswerIncomingCall()
        {
            GameManager manager = UnityEngine.Object.FindObjectOfType<GameManager>();
            if (manager == null || manager.newCallButton == null || manager.newCallButton.hidden || manager.newCallButton.conversation == null)
            {
                Speak("There is no incoming call to answer.");
                return;
            }
            manager.newCallButton.newCallAnswered();
            Speak("Call answered.");
        }

        private void MonitorGameEvents(DateTime now)
        {
            GameManager manager = UnityEngine.Object.FindObjectOfType<GameManager>();
            if (manager == null)
                return;

            if (monitoredManagerId != manager.GetInstanceID())
            {
                monitoredManagerId = manager.GetInstanceID();
                knownUnitStates.Clear();
                knownReports.Clear();
                announcedCallId = 0;
                lastConversationText = String.Empty;
                pendingConversationText = String.Empty;
                lastChoiceSignature = String.Empty;
                announcedDialogueSegments.Clear();
                monitoredConversationId = 0;
            }

            EnsureCallSpeechModeLoaded();

            List<string> updates = new List<string>();
            MonitorIncomingCall(manager, updates);
            MonitorUnits(manager, updates);
            MonitorReports(manager, updates);
            MonitorConversation(now, updates);
            if (updates.Count > 0)
                Speak(String.Join(". ", updates.ToArray()));
        }

        private void MonitorIncomingCall(GameManager manager, List<string> updates)
        {
            if (manager.newCallButton == null || manager.newCallButton.hidden || manager.newCallButton.conversation == null)
                return;
            int callId = manager.newCallButton.conversation.GetHashCode();
            if (announcedCallId == callId)
                return;
            announcedCallId = callId;
            updates.Add("Incoming 911 call. Press F6 to answer");
        }

        private void MonitorUnits(GameManager manager, List<string> updates)
        {
            if (manager.policeList == null)
                return;
            for (int i = 0; i < manager.policeList.Count; i++)
            {
                PoliceBase unit = manager.policeList[i];
                if (unit == null || unit.behaviour == null || unit.squad == null)
                    continue;
                int id = unit.GetInstanceID();
                int targetId = unit.behaviour.targetReport == null ? 0 : unit.behaviour.targetReport.GetInstanceID();
                string state = unit.behaviour.getStateName();
                string snapshot = state + "|" + targetId;
                string previous;
                if (!knownUnitStates.TryGetValue(id, out previous))
                {
                    knownUnitStates[id] = snapshot;
                    continue;
                }
                if (previous == snapshot)
                    continue;
                knownUnitStates[id] = snapshot;
                string unitName = String.IsNullOrEmpty(unit.squad.code) ? "Unit " + unit.squad.squadNo : unit.squad.code;
                string incidentName = GetTargetIncidentName(unit);
                if (state == "ONWAY")
                    updates.Add(unitName + " responding to " + incidentName);
                else if (state == "ONSITE")
                    updates.Add(unitName + " arrived at " + incidentName);
                else if (state == "FIGHTING")
                    updates.Add(unitName + " is fighting at " + incidentName);
                else if (state == "PATROL")
                    updates.Add(unitName + " is available on patrol");
                else if (state == "OFF")
                    updates.Add(unitName + " is off duty");
                else
                    updates.Add(unitName + " status " + state.ToLowerInvariant());
            }
        }

        private static string GetTargetIncidentName(PoliceBase unit)
        {
            if (unit.behaviour == null || unit.behaviour.targetReport == null || unit.behaviour.targetReport.reportInstance == null)
                return "the incident";
            ReportInstance report = unit.behaviour.targetReport.reportInstance;
            string name = report.ofType == null ? "the incident" : report.ofType.name;
            return String.IsNullOrEmpty(report.address) ? name : name + " at " + report.address;
        }

        private void MonitorReports(GameManager manager, List<string> updates)
        {
            if (manager.reportList == null)
                return;
            HashSet<int> current = new HashSet<int>();
            for (int i = 0; i < manager.reportList.Count; i++)
            {
                ReportBase reportBase = manager.reportList[i];
                if (reportBase == null || reportBase.reportInstance == null)
                    continue;
                int id = reportBase.GetInstanceID();
                current.Add(id);
                if (knownReports.ContainsKey(id))
                    continue;
                knownReports[id] = reportBase.reportInstance;
                if (manager.dutyMode)
                {
                    string name = reportBase.reportInstance.ofType == null ? "Unknown incident" : reportBase.reportInstance.ofType.name;
                    updates.Add("New incident. " + name + " at " + reportBase.reportInstance.address);
                }
            }
            List<int> removed = new List<int>();
            foreach (KeyValuePair<int, ReportInstance> item in knownReports)
            {
                if (current.Contains(item.Key))
                    continue;
                string name = item.Value.ofType == null ? "Incident" : item.Value.ofType.name;
                string result = item.Value.wayResolved.ToString();
                updates.Add(name + " cleared. " + result.ToLowerInvariant());
                removed.Add(item.Key);
            }
            for (int i = 0; i < removed.Count; i++)
                knownReports.Remove(removed[i]);
        }

        private void MonitorConversation(DateTime now, List<string> updates)
        {
            TalkDetailsPanel panel = UnityEngine.Object.FindObjectOfType<TalkDetailsPanel>();
            if (panel == null || panel.conversation == null || !panel.conversation.rolling || panel.convText == null)
            {
                lastConversationText = String.Empty;
                pendingConversationText = String.Empty;
                lastChoiceSignature = String.Empty;
                announcedDialogueSegments.Clear();
                monitoredConversationId = 0;
                return;
            }

            int conversationId = panel.conversation.GetHashCode();
            if (monitoredConversationId != conversationId)
            {
                monitoredConversationId = conversationId;
                lastConversationText = String.Empty;
                pendingConversationText = String.Empty;
                lastChoiceSignature = String.Empty;
                announcedDialogueSegments.Clear();
            }

            string visibleText = NormalizeUiText(panel.convText.text);
            if (visibleText != pendingConversationText)
            {
                pendingConversationText = visibleText;
                conversationTextChangedUtc = now;
            }
            else if (visibleText != lastConversationText && now >= conversationTextChangedUtc.AddMilliseconds(400))
            {
                string addition = visibleText;
                if (!String.IsNullOrEmpty(lastConversationText) && visibleText.StartsWith(lastConversationText))
                    addition = visibleText.Substring(lastConversationText.Length).Trim();
                if (!String.IsNullOrEmpty(addition) && ShouldSpeakDialogue(panel) && announcedDialogueSegments.Add(addition))
                    updates.Add(addition);
                lastConversationText = visibleText;
            }

            TalkButton[] buttons = Resources.FindObjectsOfTypeAll<TalkButton>();
            List<string> choices = new List<string>();
            for (int i = 0; i < buttons.Length; i++)
            {
                TalkButton button = buttons[i];
                if (button == null || !button.gameObject.activeInHierarchy || button.text == null)
                    continue;
                ConversationElement element = button.getElement();
                if (IsConversationElementUsed(element))
                    continue;
                string choice = NormalizeUiText(button.text.text);
                if (!String.IsNullOrEmpty(choice))
                    choices.Add(choice);
            }
            string signature = String.Join("|", choices.ToArray());
            if (signature == lastChoiceSignature)
                return;
            lastChoiceSignature = signature;
            if (choices.Count == 0)
                return;
            StringBuilder choiceText = new StringBuilder();
            choiceText.Append(choices.Count).Append(choices.Count == 1 ? " response. " : " responses. ");
            for (int i = 0; i < choices.Count; i++)
                choiceText.Append(i + 1).Append(", ").Append(choices[i]).Append(i + 1 == choices.Count ? ". " : "; ");
            choiceText.Append("Use up and down arrows and Enter.");
            updates.Add(choiceText.ToString());
        }

        private bool ShouldSpeakDialogue(TalkDetailsPanel panel)
        {
            EnsureCallSpeechModeLoaded();
            if (callSpeechMode == 2)
                return false;
            if (callSpeechMode == 1)
                return true;
            return panel.QuoteAudioSource == null || panel.QuoteAudioSource.clip == null;
        }

        private static bool IsConversationElementUsed(ConversationElement element)
        {
            if (element == null || System.Object.ReferenceEquals(ConversationWasClickedField, null))
                return false;
            return (bool)ConversationWasClickedField.GetValue(element);
        }

        private static void MarkConversationElementUsed(ConversationElement element)
        {
            if (element != null && !System.Object.ReferenceEquals(ConversationWasClickedField, null))
                ConversationWasClickedField.SetValue(element, true);
        }

        private void EnsureCallSpeechModeLoaded()
        {
            if (callSpeechMode >= 0)
                return;
            callSpeechMode = PlayerPrefs.GetInt("OperatorAccessibility.CallSpeechMode", 0);
            if (callSpeechMode < 0 || callSpeechMode > 2)
                callSpeechMode = 0;
        }

        private void CycleCallSpeechMode()
        {
            EnsureCallSpeechModeLoaded();
            callSpeechMode = (callSpeechMode + 1) % 3;
            PlayerPrefs.SetInt("OperatorAccessibility.CallSpeechMode", callSpeechMode);
            PlayerPrefs.Save();
            if (callSpeechMode == 0)
                Speak("Call dialogue speech smart mode. Recorded dialogue is not repeated. Unvoiced dialogue and all choices are spoken.");
            else if (callSpeechMode == 1)
                Speak("Call dialogue speech always on. All dialogue and choices are spoken.");
            else
                Speak("Call dialogue speech off. Only response choices are spoken.");
        }

        private void RecoverCallChoices(DateTime now)
        {
            if (now < nextCallRecoveryUtc)
            {
                Speak("Call recovery is cooling down.");
                return;
            }
            TalkDetailsPanel panel = UnityEngine.Object.FindObjectOfType<TalkDetailsPanel>();
            if (panel == null || panel.conversation == null || !panel.conversation.rolling)
            {
                Speak("There is no active call to recover.");
                return;
            }
            if (HasActiveTalkButtons())
            {
                Speak("Call response choices are already available. Use up and down arrows and Enter.");
                return;
            }
            if (panel.QuoteAudioSource != null && panel.QuoteAudioSource.isPlaying)
            {
                Speak("The caller is still speaking. Please wait before recovering choices.");
                return;
            }
            if (now < conversationTextChangedUtc.AddSeconds(1))
            {
                Speak("The call transcript is still updating. Please wait a moment.");
                return;
            }
            try
            {
                nextCallRecoveryUtc = now.AddSeconds(3);
                panel.getNext();
                Speak("Regenerating call response choices.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Call recovery failed: " + ex);
                Speak("Unable to recover call choices. See the accessibility log for details.");
            }
        }

        private static string NormalizeUiText(string text)
        {
            if (String.IsNullOrEmpty(text))
                return String.Empty;
            string withoutTags = Regex.Replace(text, "<.*?>", String.Empty);
            return Regex.Replace(withoutTags, "\\s+", " ").Trim();
        }

        private void DisableTutorial()
        {
            Preferences.showTutorial = false;
            PlayerPrefs.SetInt("showTutorial", 0);
            PlayerPrefs.Save();
            TutorialManager tutorial = UnityEngine.Object.FindObjectOfType<TutorialManager>();
            if (tutorial != null)
                tutorial.turnOffTutorial();
            Speak("Tutorial disabled. Return to the main menu and choose Free Game.");
        }

        private void Speak(string text)
        {
            if (String.IsNullOrEmpty(text))
                return;
            Logger.LogInfo("SPEAK: " + text);
            try
            {
                if (nvdaClientLoaded && IsNvdaRunning())
                {
                    nvdaController_cancelSpeech();
                    int speechResult = nvdaController_speakText(text);
                    nvdaController_brailleMessage(text);
                    if (speechResult == 0)
                        return;
                }
                SendToSpeechHelper(text);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Speech failed: " + ex.Message);
            }
        }

        private bool IsNvdaRunning()
        {
            try
            {
                return nvdaClientLoaded && nvdaController_testIfRunning() == 0;
            }
            catch
            {
                return false;
            }
        }

        private void SendToSpeechHelper(string text)
        {
            if (speechClient == null)
                return;
            byte[] data = Encoding.UTF8.GetBytes(text);
            speechClient.Send(data, data.Length);
        }
    }
}
