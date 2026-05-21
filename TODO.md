# Dictio — TODO

## Sprint: Demo-ready ✅

### 1. Fix PushToTalk stop bug ✅
### 2. Visual feedback during transcription ✅
### 3. Encrypt API key (DPAPI) ✅
### 4. Simplify Settings UI ✅

---

## Sprint: Onboarding & Error handling ✅

### 5. Welcome window (first launch) ✅
**Что:** показывать при первом запуске (флаг `FirstLaunchDone` в `AppSettings`).  
**Содержание:**
- Приложение живёт в трее — иконка в правом нижнем углу
- Ctrl+Space — начать/остановить запись (или держать для PushToTalk)
- Нажать на иконку в трее → открыть Settings (API ключ, микрофон, режим)
- Кнопка "Got it" закрывает окно и сохраняет флаг

**Где:** `Views/WelcomeWindow.xaml`, `AppSettings.cs`, `App.xaml.cs`

---

### 6. Open Settings if API key is missing ✅
**Что:** если пользователь нажимает Ctrl+Space, а ключ не задан — вместо попытки записи открывать Settings.  
**Где:** `App.xaml.cs` — в `StartRecording()` уже есть частичная проверка, нужно распространить на оба режима (Toggle + PushToTalk).

---

### 7. User-friendly error popup on API failure ✅
**Что:** при любом исключении во время запроса к OpenAI показывать понятное сообщение вместо технического стектрейса.  
**Формат:** небольшой WPF-попап (не MessageBox) с текстом ошибки и кнопкой "OK".  
**Категории ошибок:**
- Нет интернета / таймаут → "No internet connection"
- 401 Unauthorized → "Invalid API key — open Settings to update it"
- 429 Rate limit → "OpenAI rate limit reached, try again later"
- 5xx / прочее → "OpenAI service error (HTTP XXX)"

**Где:** `App.xaml.cs`, новый `Views/ErrorPopup.xaml`
