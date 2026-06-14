# Kuromi 🎀

Painel **kiosk** para segundo monitor com foco em **touchscreen**, feito em **Avalonia
(.NET 10)**. Fundo é o wallpaper do sistema borrado, widgets reposicionáveis e
configuráveis, e integração com GNOME/KDE via D-Bus / utilitários do sistema.

## Recursos

- **Fundo = wallpaper do sistema** (lido via `gsettings`/D-Bus), levemente borrado
  e visível. Converte formatos que o Skia não lê (JPEG-XL, HEIC…) com `magick`/`djxl`
  e faz downscale. A **cor de destaque da UI é extraída do wallpaper** (Material You-ish).
- **Grid bento responsivo** — widgets ocupam células de um grid 12×8 que **estica
  para preencher a tela**. No *modo edição* arraste o cabeçalho (move por célula),
  redimensione pelo canto (muda o span), remova (✕) e adicione (**+**). Salvo em
  `~/.config/kuromi/config.json`.
- **Sem header** — a barra de controles fica **escondida no topo** e aparece ao
  encostar o ponteiro na borda superior (também serve de alça para mover a janela).
- **Relógio** com saudação ("Bom dia/Boa tarde/Boa noite") e data em pt-BR.
- **Sistema** — CPU, RAM e GPU em anéis + histórico (sparkline). GPU via
  `nvidia-smi` (NVIDIA), `gpu_busy_percent` (AMD) ou, no **Intel (i915)**, pela
  soma de `drm-engine-*` do `fdinfo` — mesmo método do Resources/nvtop, sem root.
- **Lembretes** com prazos rápidos (+15m/+1h/+3h) e notificação via `notify-send`.
  Persistidos em `~/.config/kuromi/reminders.json`.
- **Claude** — uso do bloco de 5h, projeção, burn rate, modelos e gráfico diário,
  via `bunx ccusage` (a primeira carga baixa o pacote).
- **Aplicativos** — todos os processos agrupados por nome, com **ícone real**
  (resolvido do `.desktop` + tema de ícones; SVG rasterizado com `magick`),
  contagem e uso de RAM.
- **Ações rápidas** — botões configuráveis que rodam comandos shell (glyph + nome
  + comando, editáveis no modo edição).
- **Bluetooth** — liga/desliga, procura, conecta/desconecta dispositivos (`bluetoothctl`).
- **Controles** — brilho (`brightnessctl`), volume/mudo (`wpctl`/`pactl`) e tema
  escuro do sistema.

## Rodar

```bash
dotnet run -c Debug
```

- **F11** alterna tela cheia · **Esc** sai da tela cheia / fecha.
- A janela é sem decorações; **arraste pela barra superior "Kuromi"** para movê-la
  ao segundo monitor e então pressione F11. (No Wayland o app não posiciona a
  janela sozinho — você posiciona, conforme pedido.)

## Arquitetura

```
Models/        DTOs e config (Reminder, SystemSnapshot, ProcessGroup, ClaudeUsage…)
Services/      ShellRunner, Config, Wallpaper, SystemMonitor, Process, IconResolver,
               ClaudeUsage, Reminder, Bluetooth, AppServices (composition root)
  Desktop/     IDesktopBackend + GnomeBackend / KdeBackend (+ base comum)
Controls/      RingGauge, Sparkline, BarChart (desenhados na mão)
Converters/    BytesToHuman, PathToBitmap, StringToBrush
ViewModels/    Dashboard, Widget, MainWindow
  Widgets/     um VM por widget
Views/         MainWindow (canvas + drag/resize/fullscreen)
  Widgets/     um View por widget (resolvido pelo ViewLocator)
Styles/        KuromiTheme.axaml (paleta + estilos glass/touch)
```

## Dependências do sistema (opcionais, degradam com elegância)

`gsettings`, `gdbus`, `brightnessctl`, `wpctl`/`pactl`, `bluetoothctl`,
`magick`/`djxl`, `bun`/`bunx`, `notify-send`, `intel_gpu_top`/`nvidia-smi`.
Qualquer um ausente apenas desativa o recurso correspondente.
