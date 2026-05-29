"""Convert code-figure images to proper code listings in ВКР_ПоповМА.docx."""
import re
from pathlib import Path

DOC = Path(r"F:\IneractiveInstallationsAutomatization\_vkr_unpacked\word\document.xml")
text = DOC.read_text(encoding="utf-8")


def xml_escape(s: str) -> str:
    return (s.replace("&", "&amp;")
             .replace("<", "&lt;")
             .replace(">", "&gt;")
             .replace('"', "&quot;")
             .replace("'", "&apos;"))


def code_line(line: str) -> str:
    """Build one paragraph with a Courier New run containing the given line."""
    esc = xml_escape(line) if line else ""
    if not line:
        return ('<w:p><w:pPr><w:pStyle w:val="afe"/><w:jc w:val="both"/>'
                '<w:rPr><w:rFonts w:ascii="Courier New" w:hAnsi="Courier New" w:cs="Courier New"/>'
                '<w:sz w:val="24"/><w:szCs w:val="24"/></w:rPr></w:pPr></w:p>')
    return ('<w:p><w:pPr><w:pStyle w:val="afe"/><w:jc w:val="both"/>'
            '<w:rPr><w:rFonts w:ascii="Courier New" w:hAnsi="Courier New" w:cs="Courier New"/>'
            '<w:sz w:val="24"/><w:szCs w:val="24"/></w:rPr></w:pPr>'
            '<w:r><w:rPr><w:rFonts w:ascii="Courier New" w:hAnsi="Courier New" w:cs="Courier New"/>'
            '<w:sz w:val="24"/><w:szCs w:val="24"/></w:rPr>'
            f'<w:t xml:space="preserve">{esc}</w:t></w:r></w:p>')


def listing_block(num: str, title: str, code: str) -> str:
    """Build caption paragraph + table block for a listing."""
    lines = code.split("\n")
    cells = "".join(code_line(l) for l in lines)
    caption = ('<w:p><w:pPr><w:pStyle w:val="afe"/><w:jc w:val="both"/></w:pPr>'
               f'<w:r><w:t xml:space="preserve">Листинг {num} – </w:t></w:r>'
               '<w:r><w:rPr><w:rFonts w:eastAsia="Times New Roman" w:cs="Times New Roman"/>'
               f'<w:szCs w:val="28"/></w:rPr><w:t>{xml_escape(title)}</w:t></w:r></w:p>')
    table = ('<w:tbl>'
             '<w:tblPr><w:tblStyle w:val="afa"/><w:tblW w:w="0" w:type="auto"/>'
             '<w:tblLook w:val="04A0" w:firstRow="1" w:lastRow="0" w:firstColumn="1" '
             'w:lastColumn="0" w:noHBand="0" w:noVBand="1"/></w:tblPr>'
             '<w:tblGrid><w:gridCol w:w="10137"/></w:tblGrid>'
             '<w:tr><w:tc><w:tcPr><w:tcW w:w="10137" w:type="dxa"/></w:tcPr>'
             f'{cells}'
             '</w:tc></w:tr></w:tbl>')
    return caption + table


# ----------------------------------------------------------------------
# Code blocks
# ----------------------------------------------------------------------

SECURITY_CODE = '''from datetime import datetime, timedelta, timezone
from jose import JWTError, jwt
from passlib.context import CryptContext
from app.core.config import settings

pwd_context = CryptContext(schemes=["bcrypt"], deprecated="auto")

def create_access_token(data: dict, expires_delta: timedelta | None = None) -> str:
    to_encode = data.copy()
    expire = datetime.now(timezone.utc) + (
        expires_delta or timedelta(minutes=settings.ACCESS_TOKEN_EXPIRE_MINUTES))
    to_encode["exp"] = expire
    return jwt.encode(to_encode, settings.SECRET_KEY, algorithm=settings.ALGORITHM)

def decode_access_token(token: str) -> dict | None:
    try:
        return jwt.decode(token, settings.SECRET_KEY, algorithms=[settings.ALGORITHM])
    except JWTError:
        return None

# --- app/core/deps.py ---
async def get_current_user(
    credentials: HTTPAuthorizationCredentials = Depends(bearer_scheme),
    db: AsyncSession = Depends(get_db)) -> User:
    payload = decode_access_token(credentials.credentials)
    if payload is None:
        raise HTTPException(status_code=401, detail="Invalid token")
    result = await db.execute(select(User).where(User.id == int(payload["sub"])))
    user = result.scalar_one_or_none()
    if user is None or not user.is_active:
        raise HTTPException(status_code=401, detail="User not found or inactive")
    return user

async def require_admin(user: User = Depends(get_current_user)) -> User:
    if user.role.value != "admin":
        raise HTTPException(status_code=403, detail="Admin access required")
    return user'''

LLM_CODE = '''async def map_assets_to_preset(
    assets: list[dict],
    preset_schema: dict,
    preset_name: str) -> dict:
    """Use LLM to map uploaded assets to preset slots."""
    slots = preset_schema.get("slots", {})
    prompt = f"""You are helping map user-uploaded assets to slots in a preset.

Preset: "{preset_name}"
Available slots:
{json.dumps(slots, indent=2, ensure_ascii=False)}

Uploaded assets:
{json.dumps(assets, indent=2, ensure_ascii=False)}

Return JSON with:
- "mapping": dict of slot_key -> asset_id (int)
- "confidence": float 0-1
- "explanation": brief reasoning (in Russian)
Only return valid JSON, no markdown."""
    try:
        return await _generate_json(prompt)
    except Exception as e:
        logger.error("LLM map_assets_to_preset failed: %s", e)
        return {"mapping": {}, "confidence": 0.0,
                "explanation": f"LLM error: {e}", "tokens_used": 0}'''

BUILD_CODE = '''@celery_app.task(bind=True, max_retries=2, default_retry_delay=30)
def run_build_task(self, build_job_id: int):
    """Execute a WebGL build asynchronously."""
    with Session(sync_engine) as session:
        job = session.get(BuildJob, build_job_id)
        job.status = BuildStatus.running
        job.started_at = datetime.now(timezone.utc)
        job.attempts += 1
        session.commit()
        _add_log(session, build_job_id, BuildLogType.info, "Build started")
        try:
            config = session.get(Configuration, job.configuration_id)
            # сбор путей ассетов проекта
            asset_paths = _collect_project_assets(session, config.project_id)
            # подготовка рабочей директории и копирование ассетов
            workspace = prepare_build_directory(build_job_id, config.config_json, asset_paths)
            # запуск Unity CLI (или симулированного билда при отсутствии Unity)
            build_result = run_unity_build(workspace, build_job_id, _hw_profile(job))
            if build_result["success"]:
                # создание артефакта и iframe-кода для встраивания
                _save_artifact_and_notify(session, job, build_result)
                return {"status": "success",
                        "artifact_url": build_result["artifact_url"]}
            else:
                # повторная попытка при ошибке (до max_retries)
                if self.request.retries < self.max_retries:
                    raise self.retry(exc=Exception("Build failed"))
                job.status = BuildStatus.failed
                session.commit()
                return {"status": "failed"}
        except Exception as e:
            logger.exception("Build task error for job %d", build_job_id)
            job.status = BuildStatus.failed
            session.commit()
            return {"status": "error", "reason": str(e)}'''

MAIN_CODE = '''from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles

app = FastAPI(
    title="Interactive Installations Platform",
    description="Платформа быстрого прототипирования игровых механик",
    version="1.0.0")

# CORS
app.add_middleware(
    CORSMiddleware,
    allow_origins=settings.CORS_ORIGINS,
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"])

# API routers
app.include_router(auth.router, prefix="/api")
app.include_router(projects.router, prefix="/api")
app.include_router(assets.router, prefix="/api")
# ... всего 10 роутеров (presets, builds, llm, configurations, ...)

# Статическая раздача WebGL-сборок
app.mount("/builds", StaticFiles(directory=settings.BUILDS_DIR, html=True), name="builds")

@app.on_event("startup")
async def on_startup():
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    await _seed_admin_user()
    await _seed_default_presets()
    await _seed_default_hardware_profiles()'''

COMPOSE_CODE = '''services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: installations_platform
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports: ["5432:5432"]
    volumes: [pgdata:/var/lib/postgresql/data]

  redis:
    image: redis:7-alpine
    ports: ["6379:6379"]

  api:
    build: .
    ports: ["8000:8000"]
    env_file: [.env]
    environment:
      DATABASE_URL: postgresql+asyncpg://postgres:postgres@postgres:5432/installations_platform
      REDIS_URL: redis://redis:6379/0
    depends_on: [postgres, redis]
    command: uvicorn app.main:app --host 0.0.0.0 --port 8000

  worker:
    build: .
    env_file: [.env]
    depends_on: [postgres, redis]
    command: celery -A app.tasks.celery_app:celery_app worker --loglevel=info --concurrency=2

volumes:
  pgdata:
  assets_data:
  builds_data:'''

SERVICELOCATOR_CODE = '''public static class ServiceLocator
{
    private static readonly Dictionary<Type, object> services = new();

    public static void Register<T>(T service) where T : class
    {
        if (service == null)
            throw new ArgumentNullException(nameof(service));
        services[typeof(T)] = service;
    }

    public static T Get<T>() where T : class
    {
        if (services.TryGetValue(typeof(T), out var service))
            return (T)service;
        throw new InvalidOperationException(
            $"Service of type {typeof(T).Name} is not registered.");
    }

    public static bool TryGet<T>(out T service) where T : class
    {
        if (services.TryGetValue(typeof(T), out var obj))
        { service = (T)obj; return true; }
        service = null;
        return false;
    }

    public static void Clear() => services.Clear();
}'''

EVENTBUS_CODE = '''public static class EventBus
{
    private static readonly Dictionary<Type, Delegate> handlers = new();

    public static void Subscribe<T>(Action<T> handler)
    {
        if (handler == null) return;
        if (handlers.TryGetValue(typeof(T), out var existing))
            handlers[typeof(T)] = Delegate.Combine(existing, handler);
        else
            handlers[typeof(T)] = handler;
    }

    public static void Unsubscribe<T>(Action<T> handler)
    {
        if (handler == null) return;
        if (!handlers.TryGetValue(typeof(T), out var existing)) return;
        var remaining = Delegate.Remove(existing, handler);
        if (remaining == null) handlers.Remove(typeof(T));
        else handlers[typeof(T)] = remaining;
    }

    public static void Publish<T>(T payload)
    {
        if (!handlers.TryGetValue(typeof(T), out var existing)) return;
        foreach (var d in existing.GetInvocationList())
        {
            try { ((Action<T>)d).Invoke(payload); }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }
}'''

APICLIENT_CODE = '''public Task<TResponse> PostAsync<TRequest, TResponse>(
    string path, TRequest body, CancellationToken ct = default)
    => SendJsonAsync<TResponse>(UnityWebRequest.kHttpVerbPOST, path, body, ct);

private async Task<TResponse> SendOnceAsync<TResponse>(
    string verb, string path, object body, CancellationToken ct)
{
    using var req = new UnityWebRequest(BuildUrl(path), verb);

    if (body != null)
    {
        var json = JsonConvert.SerializeObject(body, Formatting.None,
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        var bytes = Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(bytes);
        req.SetRequestHeader("Content-Type", "application/json");
    }

    req.downloadHandler = new DownloadHandlerBuffer();
    req.timeout = DefaultTimeoutSeconds;
    req.SetRequestHeader("Accept", "application/json");

    var token = AuthTokenProvider?.Invoke();
    if (!string.IsNullOrEmpty(token))
        req.SetRequestHeader("Authorization", $"Bearer {token}");

    var op = req.SendWebRequest();
    while (!op.isDone)
    {
        if (ct.IsCancellationRequested) { req.Abort(); ct.ThrowIfCancellationRequested(); }
        await Task.Yield();
    }

    EnsureSuccess(req);
    return JsonConvert.DeserializeObject<TResponse>(req.downloadHandler.text);
}'''

UXML_CODE = '''<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <Style src="../../Theme/theme.uss" />
    <Style src="EditorScreen.uss" />

    <ui:VisualElement name="editor-root" class="editor-root">
        <!-- Верхняя панель: breadcrumb + действия -->
        <ui:VisualElement name="editor-top-bar" class="editor-top-bar">
            <ui:VisualElement class="editor-top-bar__left">
                <ui:Button name="breadcrumb-projects" text="←" />
                <ui:Label name="breadcrumb-current" text="Мой проект" />
            </ui:VisualElement>
            <ui:VisualElement class="editor-top-bar__right">
                <ui:Button name="save-btn"    text="Сохранить" />
                <ui:Button name="preview-btn" text="Превью" />
                <ui:Button name="build-btn"   text="Собрать" />
            </ui:VisualElement>
        </ui:VisualElement>

        <!-- Степпер шагов 1..6 (Ассеты, Пресет, Маппинг, Параметры, Превью, Публикация) -->
        <ui:VisualElement name="stepper" class="editor-stepper"> ... </ui:VisualElement>

        <!-- Рабочая область шага -->
        <ui:VisualElement name="step-content" class="step-content" />

        <!-- Нижняя навигация -->
        <ui:VisualElement name="step-nav" class="editor-step-nav">
            <ui:Button name="prev-step-btn" text="← Назад" />
            <ui:Button name="next-step-btn" text="Далее →" />
        </ui:VisualElement>
    </ui:VisualElement>
</ui:UXML>'''

PRESETHOST_CODE = '''public class PresetHost : IDisposable
{
    private readonly VisualElement presetStage;
    private readonly VisualElement hudHost;
    private readonly VisualElement resultHost;
    private GameObject presetGo;
    private PresetBase preset;

    public void Launch<T>(PresetContext context, string title) where T : PresetBase
    {
        Cleanup();
        presetGo = new GameObject($"Preset_{typeof(T).Name}");
        preset = presetGo.AddComponent<T>();
        preset.ScoreChanged += OnScoreChanged;
        preset.GameEnded   += OnGameEnded;
        BuildHud(title);
        preset.Initialize(context, presetStage);
        preset.StartGame();
    }

    public bool LaunchByPresetId(string presetId, PresetContext context)
    {
        switch (presetId)
        {
            case "puzzle":   Launch<PuzzlePreset>(context, "Собери пазл"); return true;
            case "sequence": Launch<SequencePreset>(context, "Повтори последовательность"); return true;
            default:
                Debug.LogWarning($"[PresetHost] Неизвестный presetId: '{presetId}'");
                return false;
        }
    }
}'''


# ----------------------------------------------------------------------
# Mapping figure -> listing (new number, title, code)
# ----------------------------------------------------------------------

LISTINGS = [
    ("3.10", "3.4",  "Фрагмент кода LLM-сервиса (llm_service.py)",       LLM_CODE),
    ("3.11", "3.5",  "Фрагмент кода сервиса сборки (build_tasks.py)",     BUILD_CODE),
    ("3.12", "3.6",  "Фрагмент кода точки входа сервера (main.py)",       MAIN_CODE),
    ("3.13", "3.7",  "Содержимое файла docker-compose.yml",               COMPOSE_CODE),
    ("3.14", "3.8",  "Фрагмент кода ServiceLocator — контейнера сервисов клиентского приложения", SERVICELOCATOR_CODE),
    ("3.15", "3.9",  "Фрагмент кода шины событий EventBus",               EVENTBUS_CODE),
    ("3.16", "3.10", "Фрагмент кода HTTP-клиента (ApiClient.PostAsync)",  APICLIENT_CODE),
    ("3.17", "3.11", "Фрагмент разметки экрана редактора (EditorScreen.uxml)", UXML_CODE),
    ("3.18", "3.12", "Фрагмент кода контейнера запуска игровых пресетов (PresetHost)", PRESETHOST_CODE),
]


# ----------------------------------------------------------------------
# Step 1: replace each figure (image paragraph + caption paragraph) with listing
# ----------------------------------------------------------------------

for fig_num, lst_num, title, code in LISTINGS:
    # Image paragraph + caption paragraph. Use [\s\S] (anything) only inside a
    # single drawing block, but never let .* span across </w:p>.
    # Constrain ALL spans to not cross </w:p> boundaries.
    pattern = re.compile(
        r'<w:p\b[^>]*>(?:(?!</w:p>).)*?<w:drawing>(?:(?!</w:p>).)*?</w:drawing>(?:(?!</w:p>).)*?</w:p>'
        r'\s*<w:p\b[^>]*>(?:(?!</w:p>).)*?<w:t(?:\s+xml:space="preserve")?>Рисунок '
        + re.escape(fig_num) + r' – ',
        re.DOTALL)
    # Need to extend match to end of caption paragraph.
    m = pattern.search(text)
    if m:
        end = text.find('</w:p>', m.end())
        if end == -1:
            raise SystemExit(f"Could not find end of caption paragraph for figure {fig_num}")
        end += len('</w:p>')
        # Override match end via a custom approach: use a span tuple
        match_start, match_end = m.start(), end
    if not m:
        raise SystemExit(f"Figure {fig_num} block not found")
    replacement = listing_block(lst_num, title, code)
    text = text[:match_start] + replacement + text[match_end:]
    print(f"Replaced figure {fig_num} -> Листинг {lst_num}: {title}")


# ----------------------------------------------------------------------
# Step 2: insert NEW Листинг 3.3 (security) after Листинг 3.2 table.
# ----------------------------------------------------------------------

m = re.search(r'<w:t xml:space="preserve">Листинг 3\.2 – </w:t>', text)
if not m:
    raise SystemExit("Listing 3.2 caption not found")
tbl_end = text.find("</w:tbl>", m.end())
if tbl_end == -1:
    raise SystemExit("Listing 3.2 table not found")
tbl_end += len("</w:tbl>")
security_block = listing_block("3.3", "Фрагмент кода модуля безопасности (security.py и deps.py)", SECURITY_CODE)
# Wrap in an empty paragraph before/after for spacing safety
text = text[:tbl_end] + security_block + text[tbl_end:]
print("Inserted new Листинг 3.3 (security)")


# ----------------------------------------------------------------------
# Step 3: update in-text references to former figures 3.10..3.18 -> listings.
# Also fix (Рисунок 3.4) -> (Листинг 3.1), (Рисунок 3.9) -> (Листинг 3.2).
# These are inline references inside running text.
# ----------------------------------------------------------------------

ref_map = {
    "3.4":  "3.1",   # models
    "3.9":  "3.2",   # config (where Рисунок 3.9 was mentioned alongside .env)
    "3.10": "3.4",
    "3.11": "3.5",
    "3.12": "3.6",
    "3.13": "3.7",
    "3.14": "3.8",
    "3.15": "3.9",
    "3.16": "3.10",
    "3.17": "3.11",
    "3.18": "3.12",
}

# Build sentinels to do all substitutions safely (avoid 3.10 -> 3.4 then 3.11 -> 3.5 collisions).
# Match forms: "Рисунок 3.N", "рисунок 3.N", "рисунке 3.N", "рисунка 3.N", "рисунку 3.N".
# Replace the entire "<word> 3.N" with "Листинг <newN>" — but we need to preserve
# proper grammar of "Листинг" (it's masculine, takes accusative endings). For inline
# parenthetical refs like "(Рисунок 3.10)" -> "(Листинг 3.4)" the nominative is fine.
# For "на рисунке 3.13" -> "в листинге 3.7" we'd want different case.
# To keep this simple and consistent with the existing "(Листинг 3.X)" style in the
# document, we will normalize all references to "Листинг 3.X" (nominative).
# We'll handle the case "на рисунке 3.13" by replacing "рисунке 3.13" too.

ref_patterns = [
    (r'Рисунок {n}\b',  'Листинг {m}'),
    (r'Рисунке {n}\b',  'Листинге {m}'),
    (r'Рисунка {n}\b',  'Листинга {m}'),
    (r'Рисунку {n}\b',  'Листингу {m}'),
    (r'рисунок {n}\b',  'листинг {m}'),
    (r'рисунке {n}\b',  'листинге {m}'),
    (r'рисунка {n}\b',  'листинга {m}'),
    (r'рисунку {n}\b',  'листингу {m}'),
]

# Sentinel substitution to avoid cascading replacements.
import uuid
SENT = f"@@SENT{uuid.uuid4().hex[:8]}@@"
# First pass: replace with sentinels
sentinel_map = {}
for i, (old_n, new_n) in enumerate(ref_map.items()):
    for pat_old, pat_new in ref_patterns:
        sent = f"{SENT}{i}_{pat_new.replace(' ', '_').replace('{m}', '')}@@"
        actual_new = pat_new.replace('{m}', new_n)
        # Track final replacement keyed by sentinel
        actual_old = pat_old.replace('{n}', re.escape(old_n))
        actual_new = pat_new.replace('{m}', new_n)
        # We need unique sentinel per pattern-old combination
        sent_key = f"{SENT}P{i}_{ref_patterns.index((pat_old, pat_new))}@@"
        sentinel_map[sent_key] = actual_new
        text, n = re.subn(actual_old, sent_key, text)
        if n:
            print(f"  Pattern '{pat_old.replace(chr(92), '').replace('{n}', old_n)}' x{n} -> '{actual_new}'")

# Second pass: replace sentinels with final text
for sent_key, final_text in sentinel_map.items():
    text = text.replace(sent_key, final_text)


# ----------------------------------------------------------------------
# Step 4: renumber figures 3.19..3.34 -> 3.10..3.25 (everywhere — captions + inline refs).
# Use sentinel substitution again to avoid collisions.
# ----------------------------------------------------------------------

# Match figure 3.NN inline refs (Russian forms) and caption text "Рисунок 3.NN".
# We only replace these specific patterns to avoid touching unrelated digits.
shift_word_patterns = [
    r'Рисунок ', r'Рисунке ', r'Рисунка ', r'Рисунку ',
    r'рисунок ', r'рисунке ', r'рисунка ', r'рисунку ',
]
shift_sentinels = {}
for old in range(34, 18, -1):
    old_s = f"3.{old}"
    new_s = f"3.{old - 9}"
    for word in shift_word_patterns:
        sent_key = f"{SENT}SHIFT_{old}_{shift_word_patterns.index(word)}@@"
        shift_sentinels[sent_key] = word + new_s
        text, n = re.subn(re.escape(word) + re.escape(old_s) + r'\b', sent_key, text)
        if n:
            print(f"  Shift '{word}{old_s}' -> '{word}{new_s}' x{n}")

for sent_key, final_text in shift_sentinels.items():
    text = text.replace(sent_key, final_text)


DOC.write_text(text, encoding="utf-8")
print("Done — wrote", DOC)
