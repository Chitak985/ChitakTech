using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ChitakTech
{
    /// <summary>
    /// Contains every single resource and recipe currently loaded by the game.
    /// Data is loaded by <see cref="KEILoader"/> and this shows the toolbar GUI.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class ChitakTechEngineeringInterface : MonoBehaviour  // CEI (minecraft reference)
    {
        /// <summary> Singleton instance </summary>
        private static ChitakTechEngineeringInterface _instance;

        /// <summary> ID of the main window </summary>
        private const int MainWindowId = 856201;
        /// <summary> ID of the resource info window </summary>
        private const int DetailWindowId = 856202;
        /// <summary> ID of the recipe info window </summary>
        private const int RecipeWindowId = 856203;

        /// <summary> Is the main window open </summary>
        private bool _mainVisible = false;
        /// <summary> Is the resource info window open </summary>
        private bool _detailVisible = false;
        /// <summary> Is the recipe info window open </summary>
        private bool _recipeVisible = false;

        /// <summary> <see cref="Rect"/> reference for the main window </summary>
        private Rect _mainRect;
        /// <summary> <see cref="Rect"/> reference for the resource info window </summary>
        private Rect _detailRect;
        /// <summary> <see cref="Rect"/> reference for the recipe info window </summary>
        private Rect _recipeRect;

        /// <summary> Current search text in the search bar </summary>
        private string _searchText = "";
        /// <summary> Main window scroll bar position </summary>
        private Vector2 _mainScroll = Vector2.zero;

        /// <summary> <see cref="ChitakTechResourceInfo"/> reference for the resource in the resource window </summary>
        private ChitakTechResourceInfo _selectedResource;
        /// <summary> Resource information window scroll bar position </summary>
        private Vector2 _detailScroll = Vector2.zero;

        /// <summary> List of <see cref="ChitakTechRecipeInfo"/> recipes shown in the recipe info window based on search </summary>
        private List<ChitakTechRecipeInfo> _filteredRecipes;
        /// <summary> Title of the recipe info window, for example "Recipes that produce E" </summary>
        private string _recipeTitle = "";
        /// <summary> Recipe information window scroll bar position </summary>
        private Vector2 _recipeScroll = Vector2.zero;

        /// <summary> Toolbar button reference </summary>
        private ApplicationLauncherButton _toolbarButton;
        /// <summary> Texture to use for the toolbar button </summary>
        private Texture2D _buttonTexture;

        /// <summary> <see cref="GUIStyle"/> for a wrapped label </summary>
        private GUIStyle _wrapLabel;
        /// <summary> <see cref="GUIStyle"/> for a centered label </summary>
        private GUIStyle _centeredLabel;
        /// <summary> <see cref="GUIStyle"/> for a bold label </summary>
        private GUIStyle _boldLabel;
        /// <summary> Are all <see cref="GUIStyle"/> loaded and usable </summary>
        private bool _stylesReady = false;

        public void Awake()
        {
            // Destroy duplicate instance
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            // Set up singleton
            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Get screen dimensions
            float sw = Screen.width;
            float sh = Screen.height;

            // Create windows based on screen dimensions
            _mainRect = new Rect(sw * 0.05f, sh * 0.1f, 700f, 500f);
            _detailRect = new Rect(sw * 0.63f, sh * 0.1f, sw / 3f, 560f);
            _recipeRect = new Rect(sw * 0.05f, sh * 0.1f, 900f, 500f);

            // Create a filled orange toolbar icon
            _buttonTexture = new Texture2D(38, 38, TextureFormat.RGBA32, false);
            Color icon = new Color(0.25f, 0.60f, 0.90f, 1f);
            Color[] pixels = new Color[38 * 38];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = icon;
            _buttonTexture.SetPixels(pixels);
            _buttonTexture.Apply();

            // Set up toolbar GameEvents
            GameEvents.onGUIApplicationLauncherReady.Add(OnLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnLauncherDestroyed);
        }

        public void OnDestroy()
        {
            if (_instance != this) return;
            GameEvents.onGUIApplicationLauncherReady.Remove(OnLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnLauncherDestroyed);
            if (_toolbarButton != null && ApplicationLauncher.Instance != null)
                ApplicationLauncher.Instance.RemoveModApplication(_toolbarButton);
            if (_buttonTexture != null) Destroy(_buttonTexture);
            _toolbarButton = null;
            _buttonTexture = null;
            _instance = null;
        }

        private void OnLauncherReady()
        {
            ApplicationLauncher launcher = ApplicationLauncher.Instance;
            if (_toolbarButton != null || launcher == null || _buttonTexture == null) return;
            _toolbarButton = launcher.AddModApplication(
                () => _mainVisible = true,
                HideAllWindows,
                null, null, null, null,
                ApplicationLauncher.AppScenes.ALWAYS,
                _buttonTexture
            );
        }

        private void OnLauncherDestroyed()
        {
            _toolbarButton = null;
            HideAllWindows();
        }

        private void OnGUI()
        {
            // Skip if nothing is open
            if (!_mainVisible && !_detailVisible && !_recipeVisible) return;
            // Load all GUI styles if needed
            EnsureStyles();
            
            // Load main window if open
            if (_mainVisible)
                _mainRect = GUILayout.Window(MainWindowId, _mainRect, DrawMainWindow,
                                             "ChitakTech Engineering Interface",
                                             HighLogic.Skin.window);
            // Load resource info window if open and there is a selected resource
            if (_detailVisible && _selectedResource != null)
                _detailRect = GUILayout.Window(DetailWindowId, _detailRect, DrawDetailWindow,
                                               "",
                                               HighLogic.Skin.window);
            // Load recipe info window if open and there are any recipes to show
            if (_recipeVisible && _filteredRecipes != null)
                _recipeRect = GUILayout.Window(RecipeWindowId, _recipeRect, DrawRecipeWindow,
                                               _recipeTitle,
                                               HighLogic.Skin.window);
        }

        /// <summary>
        /// Hides all CEI windows that can be opened.
        /// </summary>
        private void HideAllWindows()
        {
            _mainVisible = false;
            _detailVisible = false;
            _recipeVisible = false;
        }

        /// <summary>
        /// Set up <see cref="GUIStyle"/>s if they aren't already.
        /// </summary>
        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _wrapLabel = new GUIStyle(HighLogic.Skin.label) { wordWrap = true };
            _centeredLabel = new GUIStyle(HighLogic.Skin.label) { wordWrap = true, alignment = TextAnchor.MiddleCenter };
            _boldLabel = new GUIStyle(HighLogic.Skin.label) { fontStyle = FontStyle.Bold, wordWrap = true };
            _stylesReady = true;
        }

        /// <summary>
        /// Draws the entire main window with the provided id.
        /// </summary>
        private void DrawMainWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (CloseButton()) HideAllWindows();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", HighLogic.Skin.label, GUILayout.Width(55));
            _searchText = GUILayout.TextField(_searchText, HighLogic.Skin.textField);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _boldLabel, GUILayout.Width(230));
            GUILayout.Label("Abbreviation", _boldLabel, GUILayout.Width(120));
            GUILayout.Label("Cost per unit", _boldLabel, GUILayout.Width(100));
            GUILayout.EndHorizontal();

            _mainScroll = GUILayout.BeginScrollView(_mainScroll, HighLogic.Skin.scrollView);

            if (!KEILoader.IsLoaded)
                GUILayout.Label("Resources and recipes are still loading.", _wrapLabel);
            else
            {
                string filter = _searchText.Trim().ToLowerInvariant();
                foreach (ChitakTechResourceInfo res in KEILoader.Resources)
                {
                    if (!string.IsNullOrEmpty(filter) &&
                        !res.displayName.ToLowerInvariant().Contains(filter) &&
                        !res.name.ToLowerInvariant().Contains(filter))
                        continue;

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(res.displayName, HighLogic.Skin.button, GUILayout.Width(230)))
                        OpenDetailWindow(res);
                    GUILayout.Label(res.abbreviation ?? "-", HighLogic.Skin.label, GUILayout.Width(120));
                    GUILayout.Label(res.unitCost.ToString("F2"), HighLogic.Skin.label, GUILayout.Width(100));
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        /// <summary>
        /// Open the resource info window from a <see cref="ChitakTechResourceInfo"/>.
        /// </summary>
        private void OpenDetailWindow(ChitakTechResourceInfo res)
        {
            _selectedResource = res;
            _detailVisible = true;
            _detailScroll = Vector2.zero;
        }

        /// <summary>
        /// Opens CEI directly to the exact internal resource name.
        /// </summary>
        public static bool OpenResourcePage(string resourceName)
        {
            if (_instance == null || string.IsNullOrWhiteSpace(resourceName))
                return false;

            ChitakTechResourceInfo resource = _instance.FindResource(
                resourceName.Trim());
            if (resource == null) return false;
            _instance.OpenDetailWindow(resource);
            return true;
        }

        /// <summary>
        /// Draws the entire resource information window with the provided id.
        /// </summary>
        private void DrawDetailWindow(int id)
        {
            // Get the maximum width to use for labels
            float labelW = Screen.width / 3f - 60f;

            // Add header with resource as title and a closing button
            GUILayout.BeginHorizontal();
            GUILayout.Label(_selectedResource.displayName, _boldLabel, GUILayout.Width(labelW - 35f));
            if (CloseButton())
            {
                _detailVisible = false;
                _recipeVisible = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Add recipe description
            string desc = string.IsNullOrEmpty(_selectedResource.description) ? "No description available." : _selectedResource.description;
            GUILayout.Label(desc, _centeredLabel, GUILayout.Width(labelW));

            GUILayout.Space(8);

            // Start variable scrollable box
            _detailScroll = GUILayout.BeginScrollView(_detailScroll, HighLogic.Skin.scrollView);

            // Add all resource variable displays
            DrawRow("Internal Name", _selectedResource.name);
            DrawRow("Abbreviation", _selectedResource.abbreviation ?? "-");
            DrawRow("Cost per unit", _selectedResource.unitCost.ToString("F4"));
            DrawRow("Can be adjusted in VAB?", _selectedResource.isTweakable ? "Yes" : "No");
            DrawRow("Hidden resource?", _selectedResource.isVisible ? "No" : "Yes");
            DrawRow("Flow mode", _selectedResource.flowMode ?? "-");
            DrawRow("Transfer method", _selectedResource.transfer ?? "-");

            GUILayout.Space(6);

            // Create the density string
            string densityLine;

            // Handle densities
            if (CTShared.ApproxEqual(_selectedResource.density, 1f))
                densityLine = "1 unit = 1 ton";
            else if (CTShared.ApproxEqual(_selectedResource.density, 0.001f))
                densityLine = "1 unit = 1 kilogram";
            else if (CTShared.ApproxEqual(_selectedResource.density, 0.000001f))
                densityLine = "1 unit = 1 gram";
            else
                densityLine = string.Format(
                    "This resource has special density parameters. " +
                    "Every unit of this resource weighs {0:F6} kilograms.",
                    _selectedResource.density * 1000.0);

            // Handle special volume, no changes needed for normal volume
            if (!CTShared.ApproxEqual(_selectedResource.volume, 1f))
                if (densityLine != null)
                    // Special density and special volume
                    densityLine = string.Format(
                        "This resource has special density and volume parameters. " +
                        "Every unit of this resource weighs {0:F6} kilograms and each internal " +
                        "volume unit is filled by {1} of this resource.",
                        _selectedResource.density * 1000.0, _selectedResource.volume);
                else
                // Normal density and special volume
                densityLine =
                    "This resource has special volume parameters. " +
                    $"{densityLine} and each internal volume unit is filled by {_selectedResource.volume} of this resource.";

            // Add the density string
            GUILayout.Label(densityLine, _wrapLabel, GUILayout.Width(labelW));
            
            // End variable scrollable box
            GUILayout.EndScrollView();

            GUILayout.Space(6);

            // Add buttons to open recipes for this resource
            if (GUILayout.Button("Recipes that use this resource", HighLogic.Skin.button))
                OpenRecipeWindow(_selectedResource.name, isInput: true);
            if (GUILayout.Button("Recipes that produce this resource", HighLogic.Skin.button))
                OpenRecipeWindow(_selectedResource.name, isInput: false);

            // Mark window as draggable
            GUI.DragWindow();
        }

        /// <summary>
        /// Draw a string row for a variable, like so:
        /// <c>label: val</c>
        /// </summary>
        private void DrawRow(string label, string val)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", _boldLabel, GUILayout.Width(180));
            GUILayout.Label(val, _wrapLabel);
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Open the recipe info window from a resource name and
        /// whether to search for producing or consuming recipes.
        /// </summary>
        private void OpenRecipeWindow(string resourceName, bool isInput)
        {
            _filteredRecipes = KEILoader.Recipes.Where(r =>
                isInput
                    ? r.inputs.Any(i => i.resourceName == resourceName)
                    : r.outputs.Any(o => o.resourceName == resourceName)
            ).ToList();

            _recipeTitle = isInput
                ? "Recipes that use " + resourceName
                : "Recipes that produce " + resourceName;
            _recipeScroll = Vector2.zero;
            _recipeVisible = true;
        }

        /// <summary>
        /// Draws the entire recipe information window with the provided id.
        /// </summary>
        private void DrawRecipeWindow(int id)
        {
            // Add header with closing button
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (CloseButton()) _recipeVisible = false;
            GUILayout.EndHorizontal();

            // Add table headers
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _boldLabel, GUILayout.Width(200));
            GUILayout.Label("Produces heat?", _boldLabel, GUILayout.Width(100));
            GUILayout.Label("Inputs", _boldLabel, GUILayout.Width(270));
            GUILayout.Label("Outputs", _boldLabel, GUILayout.Width(270));
            GUILayout.EndHorizontal();

            // Create scrollable recipe list
            _recipeScroll = GUILayout.BeginScrollView(_recipeScroll, HighLogic.Skin.scrollView);

            // Skip list if no recipes found for search
            if (_filteredRecipes == null || _filteredRecipes.Count == 0)
                GUILayout.Label("No recipes found.", _wrapLabel);
            else
                // Go through every recipe found
                foreach (ChitakTechRecipeInfo recipe in _filteredRecipes)
                {
                    // Start recipe list element
                    GUILayout.BeginHorizontal();

                    // Add converter and part label
                    GUILayout.BeginVertical(GUILayout.Width(200));
                    GUILayout.Label(recipe.converterName, _boldLabel);
                    GUILayout.Label("(" + recipe.partTitle + ")", _wrapLabel);
                    GUILayout.EndVertical();

                    // Add "Produces heat?"
                    GUILayout.Label(recipe.generatesHeat ? "Yes" : "No", HighLogic.Skin.label, GUILayout.Width(100));

                    // Add inputs and outputs
                    CreateGUIRecipeIOList(recipe.inputs);
                    CreateGUIRecipeIOList(recipe.outputs);

                    // End list element
                    GUILayout.EndHorizontal();
                    // Box the element to be separate from others
                    GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
                }

            // End scrollable recipe list
            GUILayout.EndScrollView();
            // Mark window as draggable
            GUI.DragWindow();
        }

        /// <summary>
        /// Create a GUI list of resource inputs or outputs.
        /// Essentially turns a <c>List<ChitakTechRecipeIO></c> into a GUI list.
        /// </summary>
        private void CreateGUIRecipeIOList(List<ChitakTechRecipeIO> ios)
        {
            // Begin list
            GUILayout.BeginVertical(GUILayout.Width(270));
            
            // Skip if no IO
            if (ios.Count == 0)
                GUILayout.Label("-", _wrapLabel);
            else
                // Process each ChitakTechRecipeIO in the list
                foreach (ChitakTechRecipeIO io in ios)
                {
                    // Create the label for the element's button
                    string btnLabel = string.Format("{0:G4}x {1}/sec", io.ratio, io.resourceName);
                    // Get resource info of the used resource
                    ChitakTechResourceInfo ioRes = FindResource(io.resourceName);
                    // Add a button to open resource information if the resource was loaded
                    if (ioRes != null)
                        if (GUILayout.Button(btnLabel, HighLogic.Skin.button))
                            OpenDetailWindow(ioRes);
                    else
                    {
                        // Otherwise error out and draw a simple text
                        CTShared.LogError($"Resource {io.resourceName} is not loaded by CEI! "
                            + "Replacing resource info button with text.",
                            "ChitakTechEngineeringInterface/CreateGUIRecipeIOList");
                        GUILayout.Label(btnLabel, _wrapLabel);
                    }
                }
            
            // End list
            GUILayout.EndVertical();
        }

        /// <summary>
        /// Find a resource in loaded resource using its name.
        /// Returns a <see cref="ChitakTechResourceInfo"/>.
        /// </summary>
        private ChitakTechResourceInfo FindResource(string name)
        {
            if (!KEILoader.IsLoaded) return null;
            return KEILoader.Resources.FirstOrDefault(r => r.name == name);
        }

        /// <summary>
        /// Draw a close button, X, with width of 28.
        /// </summary>
        private bool CloseButton()
            => GUILayout.Button("X", HighLogic.Skin.button, GUILayout.Width(28));
    }
}
