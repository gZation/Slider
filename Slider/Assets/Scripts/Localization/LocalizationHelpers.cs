﻿using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine.SceneManagement;

namespace Localization
{
    internal abstract class Localizable
    {
        private string[] componentPath;

        public string IndexInComponent => index;
        protected string index;

        // Separates the index string from the hierarchy path
        public static char indexSeparator = '@';
        
        // Separates different digits within an index string
        public static char indexSeparatorSecondary = ':';

        public string FullPath => string.Join('/', componentPath) + (index != null ? indexSeparator + index : "");

        protected Localizable(string[] componentPath)
        {
            this.componentPath = componentPath;
        }

        protected static string[] GetComponentPath(Component current)
        {
            List<string> pathComponents = new();
            for (Transform go = current.transform; go != null; go = go.parent)
            {
                pathComponents.Add(go.name);
            }

            pathComponents.Reverse();
            return pathComponents.ToArray();
        }
    }

    internal sealed class ParsedLocalizable : Localizable
    {
        public string Translated { get; }

        private ParsedLocalizable(string hierarchyPath, string index, string translated) : base(hierarchyPath.Split('/'))
        {
            this.index = index;
            Translated = translated;
        }
        
        internal static ParsedLocalizable ParsePath(string fullPath, string translated = null)
        {
            string[] pathAndIndex = fullPath.Split(indexSeparator);

            if (pathAndIndex.Length < 1)
            {
                return null;
            }

            if (pathAndIndex.Length == 1)
            {
                return new ParsedLocalizable(pathAndIndex[0], null, translated);
            }

            try
            {
                return new ParsedLocalizable(pathAndIndex[0], pathAndIndex[1], translated);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }

    // Record that tracks a localizable instance in the scene, in the form of a Component + an index within that component
    internal sealed class TrackedLocalizable : Localizable
    {
        // Immediate component that this localizable tracks, several of them can track the same component
        // in the case of different indices (different dialogue numbers of the same NPC, for example)
        public T GetAnchor<T>() where T: Component => (anchor as T);
        private Component anchor;

        private TrackedLocalizable(Component c) : base(GetComponentPath(c))
        {
            anchor = c;
        }

        public TrackedLocalizable(TMP_Text tmp) : this(tmp as Component)
        {
        }

        // Track dropdown option
        public TrackedLocalizable(TMP_Dropdown dropdown, int idx) : this(dropdown as Component)
        {
            index = idx.ToString();
        }
        
        // Track dropdown itself, not particular option
        public TrackedLocalizable(TMP_Dropdown dropdown) : this(dropdown as Component)
        {
            index = null;
        }

        public TrackedLocalizable(NPC npc, int cond, int diag) : this(npc as Component)
        {
            index = cond.ToString() + Localizable.indexSeparatorSecondary + diag.ToString();
        }

        public TrackedLocalizable(DialogueDisplay display) : this(display as Component)
        {
            index = null;
        }

        public TrackedLocalizable(LocalizationInjector injector) : this(injector as Component)
        {
            index = null;
        }
    }

    internal readonly struct LocalizationConfig
    {
        public readonly string Comment;
        public readonly string Value;

        internal LocalizationConfig(string comment, string value)
        {
            Comment = comment;
            Value = value;
        }

        internal LocalizationConfig Override(string value)
        {
            return new LocalizationConfig(Comment, value);
        }
    }

    public class LocalizationFile
    {
        private static string LocalizationFolderPath(string root = null) => Path.Join(root ?? Application.streamingAssetsPath, "Localizations");

        public static List<string> LocaleList(string playerPrefLocale, string root = null)
        {
            if (Directory.Exists(LocalizationFolderPath(root)))
            {
                List<string> locales = Directory.GetDirectories(LocalizationFolderPath(root))
                    .Select(path => new FileInfo(path).Name).ToList();
                    
                locales.Sort(
                    (localeA, localeB) =>
                    {
                        if (localeA.Equals(playerPrefLocale))
                        {
                            return -1;
                        }

                        if (localeB.Equals(playerPrefLocale))
                        {
                            return 1;
                        }
                        
                        if (localeA.Equals(DefaultLocale))
                        {
                            return -1;
                        }

                        if (localeB.Equals(DefaultLocale))
                        {
                            return 1;
                        }

                        // ReSharper disable once StringCompareToIsCultureSpecific
                        return localeA.ToLower().CompareTo(localeB.ToLower());
                    });

                return locales;
            }
            else
            {
                return new List<string>();
            }
        }

        private static string LocalizationFileName(Scene scene) => scene.name + "_scene.csv";
        private static string LocalizationFileName(GameObject prefab) => prefab.name + "_prefab.csv";

        private static string LocaleGlobalFileName(string locale) => $"_{locale}_configs.csv";

        public static string LocaleGlobalFilePath(string locale, string root = null) =>
            Path.Join(LocalizationFolderPath(root), locale, LocaleGlobalFileName(locale));
        
        public static string DefaultLocale => "English";
        
        public static string AssetPath(string locale, Scene scene, string root = null) =>
            Path.Join(LocalizationFolderPath(root), locale, LocalizationFileName(scene));
        public static string AssetPath(string locale, GameObject prefab, string root = null) =>
            Path.Join(LocalizationFolderPath(root), locale, LocalizationFileName(prefab));
        
        // AT: This should really be a variable in LocalizableScene, but I'm placing it here
        //     so I don't have to re-type this whole thing as comments to the LocalizationFile
        //     class...
        internal static readonly string explainer = @"Localization files are formatted like CSV so they can be easily edited,
however formatting has strict rules for easy parsing. Most of these rules
are already handled by Excel / Libre / etc., so you don't have to worry
about them in normal usage. However, if your localization file seems to
be corrupted, these rules may be helpful for debugging purposes...
- In general, any consecutive string of \r and \n (in any order) will be
  treated as one SINGLE line break, except within an enclosed context
- The first 4 rows can be arbitrarily long
  - The first cell of first row is used to store this block of instruction
    text
  - The first 3 cells of the fourth row is used as headers
  - All other cells in the first and fourth rows are ignored
  - Columns in the second row alternate between property and value (ex.
    ' foo | 1 | bar | 3.5 | ... '
    - Any whitespaces before and after the property name are ignored
  - Columns in the third row are exclusively comments for properties in the
    second row
- Every following line can include any number of cells, but only the
  first three will be parsed (you can use the rest as comments if needed)
- The file must end in a sequence of \r and \n's
- Every cell containing a ^, \r, or \n must be enclosed in ""
  - If a "" is encountered right after a non-enclosed separator, \r, or \n,
    then the parser is said to enter an ""enclosed context"", such context
    ends immediately when a non-escaped "" that is not followed by \r, \n,
    or a separator is encountered
  - "" within each cell should be escaped using another "" in front of it,
  - If a "" is encountered in a non-enclosed context, and it is not right
    after a separator, \r, or \n, then it is entirely ignored
  - If a non-escaped "" is encountered in an enclosed context...
    - If it is followed by a separator, \r, or \n, then it is considered
      the closing quote of the current context
    - Otherwise, it is ignored
  - If a separator, \r, or \n is encountered while in an enclosed context,
    it will be preserved
";

        internal static readonly char csvSeparator = ',';

        public enum Config
        {
            NonDialogueFontAdjust,
            DialogueFontScale
        }

        public static readonly Dictionary<Config, string> ConfigToName = 
            Enum
                .GetValues(typeof(Config))
                .Cast<Config>()
                .ToDictionary(
                config => config,
                config => Enum.GetName(typeof(Config), config));

        public static readonly Dictionary<string, Config> ConfigFromName =
            ConfigToName.ToDictionary(kv => kv.Value, kv => kv.Key);

        internal static SortedDictionary<Config, LocalizationConfig> defaultConfigs = new()
        {
            {
                Config.NonDialogueFontAdjust, new LocalizationConfig(
                "String value adjusting font size for everything *except* dialogue. ex: +4 is 4 points larger, -4 is 4 points smaller", 
                "+0")
            },

            {
                Config.DialogueFontScale, new LocalizationConfig(
                    "Float value that scales font size of all dialogue text, this is an option separate because the English font of this game is *really* tiny, like about 1/3 of regular font when under the same font size. Recommended setting is around 0.3~0.6.",
                    "1.0")
            },
            
            // { fontOverridePath_name, new LocalizationConfig(
            //     "Relative path to a ttf font file from the folder containing this file, leave empty if no such font is needed", 
            //     "")
            // }
        };

        internal SortedDictionary<Config, LocalizationConfig> configs;
        internal SortedDictionary<string, ParsedLocalizable> records;

        private string locale;
        public bool IsDefaultLocale => locale.Equals(DefaultLocale);
        
        enum ParserState
        {
            Empty,
            HasPath,
            HasOriginal,
            HasTranslation,
            Inval
        }

        public static LocalizationFile MakeLocalizationFile(string locale, string filePath,
            LocalizationFile localeConfig = null)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            using var file = File.OpenRead(filePath);
            return new(locale, new StreamReader(file), localeConfig);
        }
        
        private LocalizationFile(string locale, StreamReader reader, LocalizationFile localeConfig = null)
        {
            this.locale = locale;
            
            records = new();

            int currentRow = 0; // first row is row 0
            int currentCol = 0;

            ParserState cellState = ParserState.Empty;

            string path = null;
            string orig = null;
            string trans = null;

            string property = null;

            configs = new();
            foreach (var (k, v) in (localeConfig != null ? localeConfig.configs : LocalizationFile.defaultConfigs))
            {
                configs.Add(k, new LocalizationConfig(v.Comment, v.Value));
            }

            while (true)
            {
                var (content, isNewLineStart, isEof) = ParseCell(reader);
                
                if (isNewLineStart)
                {
                    currentRow++;
                    // Debug.Log($"New line {currentRow} starts with {content}");
                    currentCol = 0;
                }
                else
                {
                    currentCol++;
                }

                if (!isEof && currentRow < 4)
                {
                    // parameter row
                    if (currentRow == 1)
                    {
                        if (currentCol % 2 == 0)
                        {
                            property = content.Trim();
                        }
                        else if (property != null)
                        {
                            if (ConfigFromName.TryGetValue(property, out var propertyEnum))
                            {
                                if (!string.IsNullOrWhiteSpace(content))
                                {
                                    configs[propertyEnum] = configs[propertyEnum].Override(content);
                                    Debug.Log($"Parsed localization config: {property} = {content}");
                                }
                            }
                            else
                            {
                                Debug.LogError($"Unrecognized localization config {property} with value {content}");
                            }
                            
                            // reset property
                            property = null;
                        }
                    }
                    
                    continue;
                }

                // on new line, commit the last entry
                if (isNewLineStart || isEof)
                {
                    if (path != null && cellState == ParserState.HasTranslation) // this should always be true but JetBrains kept giving warning, so this check is added
                    {
                        var newRecord = ParsedLocalizable.ParsePath(path, trans);
                        if (newRecord == null)
                        {
                            Debug.LogError($"Null localizable parsed {path}");
                        }
                        else
                        {
                            records.Add(path, newRecord);
                        }
                    }
                    else
                    {
                        switch (cellState)
                        {
                            case ParserState.HasPath:
                                Debug.LogError($"Inval: only path {path}");
                                break;
                            case ParserState.HasOriginal:
                                Debug.LogError($"Inval: Only path orig {path} : {orig}");
                                break;
                            default:
                                break;
                        }
                    }

                    cellState = ParserState.Empty;
                }

                if (isEof)
                {
                    break;
                }

                switch (cellState)
                {
                    case ParserState.Empty:
                        if (string.IsNullOrWhiteSpace(content))
                        {
                            cellState = ParserState.Inval;
                            break;
                        }

                        path = content;
                        cellState = ParserState.HasPath;
                        break;
                    case ParserState.HasPath:
                        cellState = ParserState.HasOriginal;
                        orig = content;
                        break;
                    case ParserState.HasOriginal:
                        if (string.IsNullOrWhiteSpace(content))
                        {
                            cellState = ParserState.Inval;
                            break;
                        }

                        trans = content;
                        cellState = ParserState.HasTranslation;
                        break;
                    case ParserState.HasTranslation:
                        break;
                    case ParserState.Inval:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            
            reader.Close();
        }
        
        private (string content, bool isNewLineStart, bool isEOF) ParseCell(StreamReader reader)
        {
            bool isNewLineStart = false;
            StringBuilder builder = new();

            bool isEnclosed = false;

            // read initial \r \n sequence before a cell begins
            // then, read character right after it only if it is a quote (in which case it is interpreted as an opening
            // quote of an enclosed cell)
            // if the reader starts at EOF, or encounters EOF after a string of \r and \n, then the file is considered
            // terminated
            while (true)
            {
                if (reader.EndOfStream)
                {
                    return (null, isNewLineStart, true);
                }

                char c = (char)reader.Peek();

                if (c is '\r' or '\n')
                {
                    isNewLineStart = true;
                    reader.Read();
                }
                else
                {
                    if (c == '"')
                    {
                        isEnclosed = true;
                        reader.Read();
                    }

                    break;
                }
            }

            bool isLastCharacterEscapingQuote = false;
            while (true)
            {
                char c = (char)reader.Peek();
                bool isThisCharacterEscapingQuote = false;

                if (c == '\r' || c == '\n' || c == csvSeparator)
                {
                    if (!isEnclosed)
                    {
                        // only read in the character if it is a separator. if it is \r or \n, leave the character for
                        // the next parser run. This is useful when there is only one such break. If it is read in
                        // instead of preserved, the next parser run will start at a non \r or \n character, and
                        // mistake the cell as on the same row as this cell
                        if (c == csvSeparator)
                        {
                            reader.Read();
                        }

                        break;
                    }
                    else
                    {
                        reader.Read();
                        builder.Append(c);
                    }
                }
                else if (c == '"')
                {
                    reader.Read(); // always read in quotes, this leaves Peek available for examining what comes after!
                    char nextAfterQuote = (char)reader.Peek();

                    if (isEnclosed)
                    {
                        // if this quote is escaped, read it and don't consider it as an escaping quote
                        if (isLastCharacterEscapingQuote)
                        {
                            isThisCharacterEscapingQuote = false;
                            builder.Append(c);
                        }
                        // if a non-escaped quote is followed by \r, \n, or separator, then it is considered the closing
                        // quote of this enclosed context
                        else if (nextAfterQuote == '\r' || nextAfterQuote == '\n' || nextAfterQuote == csvSeparator)
                        {
                            // only read the next character in if it is a separator, same reasoning as above
                            if (nextAfterQuote == csvSeparator)
                            {
                                reader.Read();
                            }
                            break;
                        }
                        // otherwise, consider this quote as an escaping quote for the next quote
                        else
                        {
                            isThisCharacterEscapingQuote = true;
                        }
                    }
                    else
                    {
                        // Having a quote in a non-enclosed cell
                        builder.Append(c);
                    }
                }
                else
                {
                    builder.Append(c);
                    reader.Read();
                }

                isLastCharacterEscapingQuote = isThisCharacterEscapingQuote;
            }

            return (builder.ToString(), isNewLineStart, false);
        }
    }

    public class LocalizableContext
    {
        private HashSet<Type> excludedTypes = new(); // currently only used by prefab localization to prevent infinite loops
        Dictionary<Type, List<TrackedLocalizable>> localizables = new();

        private SortedDictionary<LocalizationFile.Config, LocalizationConfig> configs;

        private LocalizableContext()
        {
            configs = new();
            foreach (var (k, v) in LocalizationFile.defaultConfigs)
            {
                configs.Add(k, new LocalizationConfig(v.Comment, v.Value));
            }
        }

        // Initializes a null localizable scene for global configuration purposes
        // TODO: add variable substitution support here by passing a list of <var_name>:<var_orig> values here
        private LocalizableContext(LocaleConfiguration localeConfiguration) : this()
        {
            foreach (var option in localeConfiguration.options)
            {
                if (configs.ContainsKey(option.name))
                {
                    if (!string.IsNullOrWhiteSpace(option.value))
                    {
                        configs[option.name] = configs[option.name].Override(option.value);
                    }
                }
                else
                {
                    Debug.LogError($"Unknown config called {option.name} overriden for locale {localeConfiguration.name}, not sure how to handle this...");
                }
            }
        }
        
        private LocalizableContext(Scene scene) : this()
        {
            PopulateLocalizableInstances(scene);
        }

        private LocalizableContext(GameObject prefab) : this()
        {
            excludedTypes.Add(typeof(LocalizationInjector));
            PopulateLocalizableInstances(prefab);
        }
        
        /* Following factory methods are just for clearer naming, instead of all uses of this context class being created from same named constructor */
        public static LocalizableContext ForSingleLocale(LocaleConfiguration localeConfiguration) =>
            new(localeConfiguration);

        public static LocalizableContext ForSingleScene(Scene scene) => new(scene);

        public static LocalizableContext ForSinglePrefab(GameObject prefab) => new(prefab); // TODO: implement
        
        /////////////////////////// Localizable Instance Selection  ////////////////////////////////////////////////////
        
        private static readonly Dictionary<Type, Func<Component, IEnumerable<TrackedLocalizable>>> SelectorFunctionMap = new()
        {
            { typeof(TMP_Text), component => SelectLocalizablesFromTmp(component as TMP_Text) },
            { typeof(TMP_Dropdown), component => SelectLocalizablesFromDropdown(component as TMP_Dropdown) },
            { typeof(NPC), component => SelectLocalizablesFromNpc(component as NPC) },
            { typeof(DialogueDisplay), component => new List<TrackedLocalizable>{ new (component as DialogueDisplay) } },
            { typeof(LocalizationInjector), component => new List<TrackedLocalizable>{ new (component as LocalizationInjector) }}
        };
        
        private void PopulateLocalizableInstances(Scene scene)
        {
            var rgos = scene.GetRootGameObjects();
            
            foreach (GameObject rootObj in rgos)
            {
                PopulateLocalizableInstances(rootObj);
            }
        }

        private void PopulateLocalizableInstances(GameObject rootGameObject)
        {
            foreach (Type type in SelectorFunctionMap.Keys)
            {
                if (excludedTypes.Contains(type))
                {
                    continue;
                }
                
                Component[] query = rootGameObject.GetComponentsInChildren(type, includeInactive: true);
                if (!localizables.ContainsKey(type))
                {
                    localizables.Add(type, new());
                }

                localizables[type]
                    .AddRange(query.SelectMany(component => SelectorFunctionMap[type](component)));
            }
        }

        private static IEnumerable<TrackedLocalizable> SelectLocalizablesFromTmp(TMP_Text tmp)
        {
            // skip TMP stuff under NPC and dropdown
            if (tmp.gameObject.GetComponentInParent<NPC>(includeInactive: true) != null || tmp.gameObject.GetComponentInParent<TMP_Dropdown>(includeInactive: true) != null)
            {
                return new List<TrackedLocalizable>() { };
            }

            return new List<TrackedLocalizable>() { new TrackedLocalizable(tmp) };
        }
        
        private static IEnumerable<TrackedLocalizable> SelectLocalizablesFromDropdown(TMP_Dropdown dropdown)
        {
            return dropdown.options
                .Select((_, idx) => new TrackedLocalizable(dropdown, idx))
                .Append(new TrackedLocalizable(dropdown));
        }

        private static IEnumerable<TrackedLocalizable> SelectLocalizablesFromNpc(NPC npc)
        {
            return npc.Conds.SelectMany((cond, i) =>
            {
                return cond.dialogueChain.Select((_, j) => new TrackedLocalizable(npc, i, j));
            });
        }
        
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        
        ////////////////////////// Applying Localization ///////////////////////////////////////////////////////////////
        private static readonly Dictionary<Type, Action<TrackedLocalizable, LocalizationFile>> LocalizationFunctionMap = new()
        {
            { typeof(TMP_Text), LocalizeTmp },
            { typeof(TMP_Dropdown), LocalizeDropdownOption },
            { typeof(NPC), LocalizeNpc },
            { typeof(DialogueDisplay), LocalizeDialogueDisplay },
            { typeof(LocalizationInjector), (loc, _) => loc.GetAnchor<LocalizationInjector>().Refresh() }
        };

        public void Localize(LocalizationFile file)
        {
            foreach (var (type, instances) in localizables)
            {
                foreach (TrackedLocalizable trackedLocalizable in instances)
                {
                    if (LocalizationFunctionMap.TryGetValue(type, out var localizationFuncion))
                    {
                        localizationFuncion(trackedLocalizable, file);
                    }
                }
            }
        }
        
        private static void LocalizeTmp(TrackedLocalizable tmp, LocalizationFile file)
        {
            TMP_Text tmpCasted = tmp.GetAnchor<TMP_Text>();
            string path = tmp.FullPath;

            var tmpTextTyper = tmpCasted.gameObject.GetComponent<TMPTextTyper>();
            bool tmpTextTyperExists = tmpTextTyper != null;

            if (file.records.TryGetValue(path, out var entry))
            {
                if (file.IsDefaultLocale)
                {
                    if (tmpTextTyperExists)
                    {
                        tmpTextTyper.SetFont(null, 1.0f);
                    }
                    else
                    {
                        tmpCasted.font = LocalizationLoader.DefaultUiFont;
                    }
                    // TODO: undo overflow mode
                }
                else
                {
                    tmpCasted.font = LocalizationLoader.LocalizationFont;
                    tmpCasted.overflowMode = TextOverflowModes.Overflow; // TODO: compare different overflow modes
                }
                tmpCasted.text = entry.Translated;

                if (file.configs.ContainsKey(LocalizationFile.Config.NonDialogueFontAdjust))
                {
                    string adjust = file.configs[LocalizationFile.Config.NonDialogueFontAdjust].Value;

                    if (int.TryParse(adjust, out int adjInt) && adjInt != 0)
                    {
                        string adjStr = adjInt > 0 ? "+" + adjInt : adjInt.ToString();

                        // typer exists, it will probably crunch the size tag, so the less preferred way of force
                        // changing the TMP font size,
                        // the reason why this is less preferred is that changes compound onto each other when
                        // locales are switched multiple times, and there is currently no undo mechanism
                        // TODO: fix this, add an "undo localization" option that is called before each localization
                        //       switch
                        if (tmpTextTyperExists)
                        {
                            tmpCasted.fontSize += adjInt;
                        }
                        // if typer does not exist, use the alternative approach of wrapping everything in a size tag
                        // this is more versatile since if you switch different localities the font adjustments do not
                        // compound on each other
                        else
                        {
                            tmpCasted.text = 
                                $"<size={adjStr}>" + tmpCasted.text + $"</size>";
                        }
                    }
                }
            }
            else
            {
                Debug.LogWarning($"{path}: NOT FOUND");
            }
        }
        
        private static void LocalizeDropdownOption(TrackedLocalizable dropdownOption, LocalizationFile file)
        {
            TMP_Dropdown dropdown = dropdownOption.GetAnchor<TMP_Dropdown>();

            // particular option in dropdown
            if (dropdownOption.IndexInComponent != null)
            {
                int idx = int.Parse(dropdownOption.IndexInComponent);
                
                string path = dropdownOption.FullPath;
                if (file.records.TryGetValue(path, out var entry))
                {
                    dropdown.options[idx].text = entry.Translated;
                    
                    if (file.configs.ContainsKey(LocalizationFile.Config.NonDialogueFontAdjust))
                    {
                        string adjust = file.configs[LocalizationFile.Config.NonDialogueFontAdjust].Value;

                        if (int.TryParse(adjust, out int adjInt) && adjInt != 0)
                        {
                            string adjStr = adjInt > 0 ? "+" + adjInt.ToString() : adjInt.ToString();
                                
                            dropdown.options[idx].text = 
                                $"<size={adjStr}>" + dropdown.options[idx].text + $"</size>";
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"{path}: NOT FOUND");
                }
            }
            // entire dropdown itself
            else
            {
                if (!file.IsDefaultLocale)
                {
                    dropdown.itemText.font = LocalizationLoader.LocalizationFont;
                    dropdown.itemText.overflowMode = TextOverflowModes.Overflow; // TODO: compare different overflow modes
                }
                else
                {
                    dropdown.itemText.font = LocalizationLoader.DefaultUiFont;
                    // TODO: undo overflow mode for default locale
                }
            }
        }

        private static void LocalizeNpc(TrackedLocalizable npc, LocalizationFile file)
        {
            string path = npc.FullPath;
            if (file.records.TryGetValue(path, out var entry))
            {
                var idx = npc.IndexInComponent.Split(Localizable.indexSeparatorSecondary);

                int idxCond = int.Parse(idx[0]);
                int idxDiag = int.Parse(idx[1]);

                NPC npcCasted = npc.GetAnchor<NPC>();
                npcCasted.Conds[idxCond].dialogueChain[idxDiag].DialogueLocalized = entry.Translated;
            }
            else
            {
                Debug.LogWarning($"{path}: NOT FOUND");
            }
        }

        private static void LocalizeDialogueDisplay(TrackedLocalizable display, LocalizationFile file)
        {
            string adjStr = file.configs[LocalizationFile.Config.DialogueFontScale].Value;
            try
            {
                float adjFlt = float.Parse(adjStr);

                DialogueDisplay displayCasted = display.GetAnchor<DialogueDisplay>();

                if (file.IsDefaultLocale)
                {
                    displayCasted.SetFont(null, adjFlt, true);
                }
                else
                {
                    displayCasted.SetFont(LocalizationLoader.LocalizationFont, adjFlt, false);
                }
            }
            catch (FormatException)
            {
                Debug.LogError($"Invalid formatted dialogue font scale {adjStr}");
            }
        }
        
        /////////////////////////// Serialization //////////////////////////////////////////////////////////////////////
        private static readonly Dictionary<Type, Func<TrackedLocalizable, string>> SerializationFunctionMap = new()
        {
            { typeof(TMP_Text), SerializeTmp },
            { typeof(TMP_Dropdown), SerializeDropdownOption },
            { typeof(NPC), SerializeNpc }
        };
        
        public string Serialize(bool serializeConfigurationDefaults, LocalizationFile referenceFile)
        {
            StringBuilder builder = new StringBuilder();

            char sep = LocalizationFile.csvSeparator;
            
            // file format explainer
            builder.Append($"\"{LocalizationFile.explainer.Replace("\"", "\"\"")}\"{sep}\r\n");
            
            // properties and values
            foreach (var (name, defaults) in configs)
            {
                var nameStr = LocalizationFile.ConfigToName[name];
                
                if (serializeConfigurationDefaults)
                {
                    builder.Append($"\"{nameStr.Replace("\"", "\"\"")}\"{sep}\"{defaults.Value.Replace("\"", "\"\"")}\"{sep}");
                }
                else
                {
                    builder.Append($"\"{nameStr.Replace("\"", "\"\"")}\"{sep}\" \"{sep}");
                }
            }
            builder.Append("\r\n");
            
            // property comments
            foreach (var (name, defaults) in configs)
            {
                builder.Append($"\"{defaults.Comment.Replace("\"", "\"\"")}\"{sep}\" \"{sep}");
            }
            builder.Append("\r\n");
            
            // headers 
            builder.Append($"\"Path\"{sep}\"Orig\"{sep}\"Translation\"{sep}\r\n");

            SortedDictionary<string, string> data = SerializeTrackedLocalizables();
            
            foreach (var (_path, _orig) in data)
            {
                // use double double-quotes to escape double-quotes

                string path = _path.Replace("\"", "\"\"");
                string orig = _orig.Replace("\"", "\"\"");

                string translated = orig;

                if (referenceFile != null)
                {
                    if (referenceFile.records.TryGetValue(_path, out var referenceTranslation))
                    {
                        translated = referenceTranslation.Translated;
                    }
                    else
                    {
                        Debug.LogError($"Could not migrate {_path} with original text {_orig}");
                    }
                }
                
                builder.Append($"\"{path}\"{sep}\"{orig}\"{sep}\"{translated}\"{sep}\r\n"); // for skeleton file, just use original as the translation
            }

            return builder.ToString();
        }
        
        private SortedDictionary<string, string> SerializeTrackedLocalizables()
        {
            SortedDictionary<string, string> result = new();
            
            Dictionary<Type, Action<TrackedLocalizable>> serializationMappingAdaptor = new();
            foreach (var (type, serialize) in SerializationFunctionMap)
            {
                serializationMappingAdaptor.Add(type, localizable =>
                {
                    string orig = serialize(localizable);
                    if (string.IsNullOrWhiteSpace(orig))
                    {
                        return;
                    }
                    string path = localizable.FullPath;
                    if (!result.TryAdd(path, orig))
                    {
                        Debug.LogError($"Duplicate path: {path}");
                    }
                });
            }

            foreach (var (type, instances) in localizables)
            {
                foreach (TrackedLocalizable localizable in instances)
                {
                    if (serializationMappingAdaptor.TryGetValue(type, out var serializationFunction))
                    {
                        serializationFunction(localizable);
                    }
                }
            }

            return result;
        }

        private static string SerializeTmp(TrackedLocalizable tmp)
        {
            return tmp.GetAnchor<TMP_Text>().text;
        }
        
        private static string SerializeDropdownOption(TrackedLocalizable dropdownOption)
        {
            if (dropdownOption.IndexInComponent != null)
            {
                return dropdownOption.GetAnchor<TMP_Dropdown>().options[int.Parse(dropdownOption.IndexInComponent)].text;
            }
            // for dropdown itself, not particular options
            else
            {
                return null;
            }
        }
        
        private static string SerializeNpc(TrackedLocalizable npc)
        {
            if (npc.IndexInComponent == null)
            {
                return null;
            }
            
            var idx = npc.IndexInComponent.Split(Localizable.indexSeparatorSecondary);

            int idxCond = int.Parse(idx[0]);
            int idxDiag = int.Parse(idx[1]);

            NPC npcCasted = npc.GetAnchor<NPC>();
            
            return npcCasted.Conds[idxCond].dialogueChain[idxDiag].dialogue;
        }
    }
}