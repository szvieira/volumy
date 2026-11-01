namespace Volumy.Localization;

/// <summary>
/// UI strings in English (default) and Brazilian Portuguese.
/// XAML binds them with {x:Bind loc:S.Name}; they are read once per window, so
/// changing the language restarts the app.
/// </summary>
public static class S
{
    public const string English = "en";
    public const string Portuguese = "pt-BR";

    public static readonly (string Code, string Name)[] Languages =
    {
        (English, "English"),
        (Portuguese, "Português (Brasil)"),
    };

    private static bool _pt;

    public static string Language { get; private set; } = English;

    public static void SetLanguage(string? code)
    {
        Language = code == Portuguese ? Portuguese : English;
        _pt = Language == Portuguese;
    }

    private static string T(string en, string pt) => _pt ? pt : en;

    // ---------- General ----------
    public static string AppName => "Volumy";
    public static string Audio => T("Audio", "Áudio");
    public static string Apps => T("Apps", "Apps");
    public static string Mute => T("Mute", "Silenciar");
    public static string Muted => T("Muted", "Mudo");
    public static string Save => T("Save", "Salvar");
    public static string Cancel => T("Cancel", "Cancelar");
    public static string Delete => T("Delete", "Excluir");
    public static string On => T("On", "Ativado");
    public static string Off => T("Off", "Desativado");

    // ---------- Flyout ----------
    public static string ConfigureProfiles => T("Configure profiles", "Configurar perfis");
    public static string NewProfileFromCurrent => T("New profile from current state", "Novo perfil a partir do estado atual");
    public static string MoreProfiles => T("More profiles", "Mais perfis");
    public static string NewProfile => T("New profile", "Novo perfil");
    public static string OutputDevice => T("Output device", "Dispositivo de saída");
    public static string MuteOutput => T("Mute output", "Silenciar saída");
    public static string OutputVolume => T("Output volume", "Volume da saída");
    public static string Microphone => T("Microphone", "Microfone");
    public static string MuteMicrophone => T("Mute microphone", "Silenciar microfone");
    public static string MicrophoneVolume => T("Microphone volume", "Volume do microfone");
    public static string NoApps => T("No apps playing audio right now", "Nenhum app com áudio agora");
    public static string HotkeyActivates(string hotkey) => T($"{hotkey} activates this profile", $"{hotkey} ativa este perfil");
    public static string SaveStateTo(string profile) => T($"Save current state to {profile}", $"Salvar estado no perfil {profile}");
    public static string SaveState => T("Save current state", "Salvar estado");
    public static string StateSavedTo(string profile) => T($"✓ State saved to {profile}", $"✓ Estado salvo no perfil {profile}");
    public static string AppCount(int n) => n == 1 ? "1 app" : $"{n} apps";

    // ---------- Toast / tray ----------
    public static string ProfileActive(string profile) => T($"{profile} profile active", $"Perfil {profile} ativo");
    public static string DefaultOutput => T("default output", "saída padrão");
    public static string DefaultMicrophone => T("default microphone", "microfone padrão");
    public static string HotkeyUnavailable => T("Shortcut unavailable", "Atalho indisponível");
    public static string HotkeyTaken(string keys) => T($"Already used by another app: {keys}", $"Já usado por outro app: {keys}");
    public static string MenuSaveToActive => T("Save current state to active profile", "Salvar estado atual no perfil ativo");
    public static string MenuConfigure => T("Configure profiles…", "Configurar perfis…");
    public static string MenuOpenJson => T("Open JSON file", "Abrir arquivo JSON");
    public static string MenuReload => T("Reload profiles", "Recarregar perfis");
    public static string MenuQuit => T("Quit", "Sair");
    public static string ProfileN(int n) => T($"Profile {n}", $"Perfil {n}");

    // ---------- Volume / mic OSD ----------
    public static string Volume(int percent) => $"Volume {percent}%";
    public static string SoundMuted => T("Sound muted", "Som silenciado");
    public static string MicMuted => T("Microphone muted", "Microfone silenciado");
    public static string MicOn => T("Microphone on", "Microfone ligado");
    public static string MicOffTag => T("mic off", "mic desligado");
    public static string TrayHint => T("Scroll: volume · Middle-click: mute", "Rolar: volume · Clique do meio: silenciar");

    // ---------- Profiles window ----------
    public static string ProfilesTitle => T("Audio profiles", "Perfis de áudio");
    public static string ProfilesSubtitle => T("Each profile stores default devices, volumes and per-app rules.",
                                               "Cada perfil guarda dispositivos padrão, volumes e regras por app.");
    public static string Identity => T("Identity", "Identificação");
    public static string Name => T("Name", "Nome");
    public static string Hotkey => T("Global shortcut", "Atalho global");
    public static string PressShortcut => T("Press a shortcut…", "Pressione o atalho…");
    public static string ShortcutNeedsModifier => T("Use Ctrl, Alt, Shift or Win + a key", "Use Ctrl, Alt, Shift ou Win + uma tecla");
    public static string HotkeyHelp => T("Click the field and press the keys (e.g. Ctrl + Alt + 1). Backspace clears it.",
                                         "Clique no campo e pressione as teclas (ex.: Ctrl + Alt + 1). Backspace apaga.");
    public static string Color => T("Color", "Cor");
    public static string SwitchSound => T("Switch sound", "Som de troca");
    public static string Preview => T("Preview", "Ouvir");
    public static string ChooseFile => T("Choose file…", "Escolher arquivo…");
    public static string SoundHelp => T("Plays on the new output, so you know the switch worked. WAV, MP3, M4A or OGG.",
                                        "Toca na nova saída, para você saber que trocou. WAV, MP3, M4A ou OGG.");
    public static string CustomSound(string file) => T($"Custom: {file}", $"Personalizado: {file}");
    public static string Activation => T("Activation", "Ativação");
    public static string AutoSwitchDevice => T("Switch automatically when this device connects", "Ativar sozinho quando este dispositivo conectar");
    public static string RestoreOnStart => T("Restore the last profile when the app opens", "Restaurar o último perfil ao abrir o app");
    public static string PlaySoundToggle => T("Play a sound when switching profiles", "Tocar um som ao trocar de perfil");
    public static string DefaultDevices => T("Default devices", "Dispositivos padrão");
    public static string Output => T("Output", "Saída");
    public static string Communications => T("Communications (Discord, Teams, Meet)", "Comunicação (Discord, Teams, Meet)");
    public static string DeviceVolumeHelp => T("Each device's volume is whatever it was when you captured the state.",
                                               "O volume de cada dispositivo é o que estava quando você capturou o estado.");
    public static string AppRules => T("Per-app rules", "Regras por app");
    public static string AddPlayingApps => T("Add apps playing now", "Adicionar apps tocando agora");
    public static string NoRules => T("No rules. Apps without a rule keep their current volume.",
                                      "Nenhuma regra. Apps sem regra mantêm o volume que já tinham.");
    public static string RemoveRule => T("Remove rule", "Remover regra");
    public static string DeleteProfile => T("Delete profile", "Excluir perfil");
    public static string CaptureState => T("Capture current state", "Capturar estado atual");
    public static string CaptureStateTip => T("Replaces this profile's devices and volumes with what is set right now",
                                              "Substitui dispositivos e volumes deste perfil pelo que está configurado agora");
    public static string SaveProfile => T("Save profile", "Salvar perfil");
    public static string General => T("General", "Geral");
    public static string MicHotkeyLabel => T("Mic mute shortcut", "Atalho para silenciar o microfone");
    public static string MicHotkeyInUse => T("That shortcut already mutes the microphone.", "Esse atalho já silencia o microfone.");
    public static string MicHotkeySaved => T("Mic shortcut saved.", "Atalho do microfone salvo.");
    public static string StartWithWindows => T("Start with Windows", "Iniciar com o Windows");
    public static string LanguageLabel => T("Language", "Idioma");
    public static string LanguageRestart => T("The app restarts to change the language.", "O app reinicia para trocar o idioma.");

    public static string None => T("(none)", "(nenhum)");
    public static string DontChange => T("(don't change)", "(não mudar)");
    public static string SameAsOutput => T("(same as output)", "(igual à saída)");
    public static string Disconnected => T("disconnected", "desconectado");

    public static string ActiveTag => T("active", "ativo");
    public static string NeedName => T("Give the profile a name.", "Dê um nome ao perfil.");
    public static string BadHotkey => T("Invalid shortcut. Example: Ctrl+Alt+1", "Atalho inválido. Exemplo: Ctrl+Alt+1");
    public static string HotkeyInUse(string profile) => T($"The {profile} profile already uses this shortcut.", $"O perfil {profile} já usa esse atalho.");
    public static string SavedApplied => T("Saved and applied.", "Salvo e aplicado.");
    public static string Saved => T("Saved.", "Salvo.");
    public static string Captured => T("Current state captured.", "Estado atual capturado.");
    public static string Created => T("Profile created from the current state. Name it and save.", "Perfil criado com o estado atual. Dê um nome e salve.");
    public static string DeleteConfirm(string profile) => T($"Delete the {profile} profile?", $"Excluir o perfil {profile}?");
    public static string CantUndo => T("This can't be undone.", "Essa ação não pode ser desfeita.");
    public static string NoNewApps => T("No new apps playing right now.", "Nenhum app novo tocando agora.");
    public static string AppsAdded(int n) => T($"{n} app(s) added. Save to keep them.", $"{n} app(s) adicionados. Salve para manter.");
    public static string SoundFileError => T("Couldn't use that file.", "Não deu para usar esse arquivo.");

    // ---------- Colors ----------
    public static string ColorName(string? hex) => hex switch
    {
        null => T("White", "Branco"),
        "#7FB2FF" => T("Blue", "Azul"),
        "#5EE3CD" => T("Teal", "Verde-água"),
        "#8BD17C" => T("Green", "Verde"),
        "#FFD166" => T("Yellow", "Amarelo"),
        "#FFB86B" => T("Orange", "Laranja"),
        "#FF8A8A" => T("Red", "Vermelho"),
        "#FF9CC8" => T("Pink", "Rosa"),
        "#C3A2FF" => T("Lilac", "Lilás"),
        _ => hex,
    };

    // ---------- Built-in sounds ----------
    public static string SoundName(string id) => id switch
    {
        "chime" => T("Chime", "Sino"),
        "bubble" => T("Bubbles", "Bolhas"),
        "pulse" => T("Pulse", "Pulso"),
        "glass" => T("Glass", "Vidro"),
        "blip" => T("Blip", "Bip"),
        "warp" => T("Warp", "Dobra"),
        "droplet" => T("Droplet", "Gota"),
        "orbit" => T("Orbit", "Órbita"),
        _ => id,
    };
}
