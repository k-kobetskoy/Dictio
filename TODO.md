# Dictio — TODO

## В работе

### 1. chunking_strategy controls в Debug окне
**Что:** добавить слайдеры/поля для `prefix_padding_ms` и `silence_duration_ms` в `DebugWindow`,
чтобы при повторной транскрипции можно было быстро перебирать параметры VAD и смотреть на результат.  
**Статус:** SDK 2.10.0 не экспонирует `chunking_strategy` в `AudioTranscriptionOptions` — нужно либо ждать новой версии SDK, либо передавать через raw JSON запрос.  
**Где:** `Views/DebugWindow.xaml/.cs`, `Services/TranscriptionService.cs`
