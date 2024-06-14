using System;
using System.Linq;
using Localization;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(SettingRetriever))]
public class LocaleSelector : MonoBehaviour
{
    private SettingRetriever retriever;
    
    private void Start()
    {
        retriever = GetComponent<SettingRetriever>();
        
        Dropdown.ClearOptions();
        
        var sortedOptions = LocalizationFile.LocaleList(retriever.ReadSettingValue() as string).Select(locale =>
        {
            TMP_Dropdown.OptionData data = new()
            {
                text = locale
            };
            return data;
        }).ToList();
        
        Dropdown.options.AddRange(sortedOptions);
        Dropdown.value = 0;
        Dropdown.RefreshShownValue();

        // if previously configured locale is no longer valid (not present as a mod, etc.), then the next most prominent
        // locale will be selected instead. this is just English because of how locales are sorted
        if (!sortedOptions[0].text.Equals(retriever.ReadSettingValue() as string))
        {
            retriever.WriteSettingValue(sortedOptions[0].text);
        }

        if (sortedOptions.Count == 1)
        {
            gameObject.SetActive(false);
        }
    }

    [SerializeField]
    private GameObject ShowHide;
    
    [SerializeField]
    private TMP_Dropdown Dropdown;


    public void OnSelectorIconClicked()
    {
        ShowHide.SetActive(false);
        Dropdown.gameObject.SetActive(true);
    }

    public void OnSelectionValueChanged()
    {
        Dropdown.gameObject.SetActive(false);
        ShowHide.SetActive(true);

        string oldLocale = retriever.ReadSettingValue() as string ?? "";
        string selection = Dropdown.options[Dropdown.value].text;

        // don't change anything if "switching" to the same language
        if (!oldLocale.Equals(selection))
        {
            return;
        }
        
        retriever.WriteSettingValue(selection);
        
        bool isEnglish = selection.Equals(LocalizationFile.DefaultLocale);
        // non English font does not have outline (TMP has outline but it seems to be UI text only)
        // instead of messing with outlines just force text background to be on, player can toggle
        // it back if they want to
        if (!isEnglish)
        {
            SettingsManager.Setting(Settings.HighContrastTextEnabled).SetCurrentValue(true);
        }

        // no need to reload stuff if switching from engish to non-english
        if (!oldLocale.Equals(LocalizationFile.DefaultLocale))
        {
            string currentSceneName = SceneManager.GetActiveScene().name;
            SceneManager.LoadScene(currentSceneName);
        }
        
        // Don't put anything here... there's a force scene reload in the setting change event (see SettingsManager)
    }
}
