using System;
using System.IO;
using Newtonsoft.Json;
using Recharge.ModApi;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IgtapLab
{
    public sealed class LabMod : IRechargeMod
    {
        public string Id => "chandlerferry.igtaplab";
        public string DisplayName => "IGTAP Lab";
        public Version Version => new Version(1, 0, 0);

        internal static IRechargeHost Host;
        internal static WorldDef World;
        GameObject _overlay;

        public void OnLoad(IRechargeHost host)
        {
            Host = host;
            string path = Path.Combine(Path.GetDirectoryName(typeof(LabMod).Assembly.Location), "labs", "world.json");
            World = JsonConvert.DeserializeObject<WorldDef>(File.ReadAllText(path));
            _overlay = new GameObject("IgtapLab overlay");
            UnityEngine.Object.DontDestroyOnLoad(_overlay);
            _overlay.AddComponent<LabOverlay>();
            host.OnUpdate += LabSession.Update;
            SceneManager.sceneUnloaded += LabSession.SceneUnloaded;
            Install(host.PauseMenu);
            PauseMenuHelper.OnMenuReady(host, Install);
            host.Log(World.labs.Count + " labs loaded.");
        }

        public void OnUnload()
        {
            LabSession.Exit();
            Host.OnUpdate -= LabSession.Update;
            SceneManager.sceneUnloaded -= LabSession.SceneUnloaded;
            if (_overlay != null) UnityEngine.Object.Destroy(_overlay);
        }

        static void Install(pauseMenuScript menu)
        {
            GameObject panel = PauseMenuHelper.AddPanelRow(menu, "IgtapLabs", "Labs");
            if (panel == null) return;
            LabPanel labs = panel.GetComponent<LabPanel>();
            if (labs == null) labs = LabPanel.Build(panel);
            labs.Menu = menu;
        }

        sealed class LabPanel : MonoBehaviour
        {
            const int PerPage = 8;
            public pauseMenuScript Menu;
            GameObject[] _rows;
            TMP_Text _pageText;
            GameObject _nextDemo, _exit;
            int _page;

            static int Pages => Math.Max(1, (World.labs.Count + PerPage - 1) / PerPage);

            public static LabPanel Build(GameObject panel)
            {
                PauseMenuHelper.NormalizePanelLayout(panel);
                PanelLayout.Apply(panel, new Vector2(720f, 700f));
                LabPanel labs = panel.AddComponent<LabPanel>();
                TMP_FontAsset font = panel.GetComponentInChildren<TMP_Text>(true).font;
                Transform parent = panel.transform;

                labs._rows = new GameObject[PerPage];
                for (int i = 0; i < PerPage; i++)
                {
                    int slot = i;
                    labs._rows[i] = PanelWidgets.CreateButton(parent, font, "Lab" + i, new Vector2(0f, 230f - i * 50f), new Vector2(620f, 44f), "",
                        fontSize: 22f, align: TextAlignmentOptions.MidlineLeft);
                    OnClick(labs._rows[i], () => labs.Pick(() => LabSession.Enter(World.labs[labs._page * PerPage + slot], 0)));
                }
                OnClick(PanelWidgets.CreateButton(parent, font, "Prev", new Vector2(-150f, -180f), new Vector2(60f, 44f), "<", 26f), () => labs.Turn(-1));
                OnClick(PanelWidgets.CreateButton(parent, font, "Next", new Vector2(150f, -180f), new Vector2(60f, 44f), ">", 26f), () => labs.Turn(1));
                labs._pageText = PanelWidgets.CreateLabel(parent, font, new Vector2(0f, -180f), new Vector2(200f, 44f), "", 22f);
                labs._nextDemo = PanelWidgets.CreateButton(parent, font, "NextDemo", new Vector2(-160f, -240f), new Vector2(300f, 44f), "", 22f);
                OnClick(labs._nextDemo, () => labs.Pick(LabSession.NextDemo));
                labs._exit = PanelWidgets.CreateButton(parent, font, "ExitLab", new Vector2(160f, -240f), new Vector2(300f, 44f), "Exit lab", 22f);
                OnClick(labs._exit, () => labs.Pick(LabSession.Exit));
                labs.Refresh();
                return labs;
            }

            static void OnClick(GameObject button, Action action) => button.GetComponent<Button>().onClick.AddListener(() => action());

            void OnEnable()
            {
                if (_rows != null) Refresh();
            }

            void Turn(int by)
            {
                _page = (_page + by + Pages) % Pages;
                Refresh();
            }

            void Refresh()
            {
                _page = Math.Min(_page, Pages - 1);
                for (int i = 0; i < PerPage; i++)
                {
                    int index = _page * PerPage + i;
                    _rows[i].SetActive(index < World.labs.Count);
                    if (index >= World.labs.Count) continue;
                    LabDef lab = World.labs[index];
                    TMP_Text label = _rows[i].GetComponentInChildren<TMP_Text>();
                    label.text = lab.name;
                    label.color = lab == LabSession.Current ? PanelWidgets.DefaultAccentColor : Color.white;
                }
                _pageText.text = (_page + 1) + " / " + Pages;

                LabDef current = LabSession.Current;
                _nextDemo.SetActive(current != null);
                _exit.SetActive(current != null);
                if (current == null) return;
                int demo = current.demos.IndexOf(LabSession.Demo);
                _nextDemo.GetComponentInChildren<TMP_Text>().text = "Next demo (" + (demo + 1) + "/" + current.demos.Count + ")";
            }

            void Pick(Action action)
            {
                try { action(); }
                catch (Exception e) { Host.LogError(e.ToString()); }
                gameObject.SetActive(false);
                GameObject main = PauseMenuHelper.MainBit(Menu);
                if (main != null) main.SetActive(true);
                if (Menu != null && Menu.menuOpen) Menu.menuButtonPressed();
            }
        }
    }
}
