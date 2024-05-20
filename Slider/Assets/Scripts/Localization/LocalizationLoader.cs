using System;
using System.IO;
using Localization;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

using LocalizationFile = Localization.LocalizationFile;

[RequireComponent(typeof(SettingRetriever))]
public class LocalizationLoader : Singleton<LocalizationLoader>
{
    [SerializeField]
    private TMP_FontAsset localizationFont;
    
    [SerializeField]
    private TMP_FontAsset defaultUiFont;

    public static TMP_FontAsset LocalizationFont => _instance.localizationFont;
    public static TMP_FontAsset DefaultUiFont => _instance.defaultUiFont;

    private string locale = LocalizationFile.DefaultLocale;
    
    private void Awake()
    {
        InitializeSingleton();
    }

    private void Start()
    {
        RefreshLocalization(SceneManager.GetActiveScene());
    }

    public static void RefreshLocalization()
    {
        if (_instance != null)
        {
            _instance.RefreshLocalization(SceneManager.GetActiveScene()); // do not use GameObject.Scene since it will return the non destructable scene instead!
        }
    }

    public void RefreshLocalization(Scene scene)
    {
        string lastLocale = locale;
        locale = GetComponent<SettingRetriever>().ReadSettingValue() as string;

        if (lastLocale.Equals(locale))
        {
            return;
        }
        
        LocalizableContext loaded = LocalizableContext.ForSingleScene(scene);
        LocalizableContext persistent = LocalizableContext.ForSingleScene(GameManager.instance.gameObject.scene);

        LocalizationFile localeConfig = null; // TODO: only load this on locale change?
        LocalizationFile loadedAsset = null;

        string localeConfigsPath = LocalizationFile.LocaleGlobalFilePath(locale);
        if (File.Exists(localeConfigsPath))
        {
            localeConfig = new(locale, new StreamReader(File.OpenRead(localeConfigsPath)));
        }

        string localizationPath = LocalizationFile.LocaleAssetPath(locale, scene); // TODO: use actual locale
        if (File.Exists(localizationPath))
        {
            loadedAsset = new(locale, new StreamReader(File.OpenRead(localizationPath)), localeConfig);
        }
        
        if (loadedAsset == null)
        {
            Debug.LogError($"Locale file does not exist {locale}");
            return;
        }
        
        loaded.Localize(loadedAsset);
        persistent.Localize(loadedAsset);
    }
}