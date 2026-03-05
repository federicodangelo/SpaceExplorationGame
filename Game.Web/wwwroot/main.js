// main.js — Bootstrap for .NET WASM runtime + game module implementations
import { dotnet } from './_framework/dotnet.js';
import { createCanvas2DRenderer } from './renderer-canvas2d.js';
import { createWebGLRenderer } from './renderer-webgl.js';

// ────────────────────────────────────────────────────────────────
// Renderer selection: ?renderer=webgl enables WebGL, default = canvas2d
// ────────────────────────────────────────────────────────────────
const _rendererParam = new URLSearchParams(window.location.search).get('renderer');
const USE_CANVAS2D = _rendererParam === 'canvas2d';
console.log(`[SEG] Renderer: ${USE_CANVAS2D ? 'canvas2d' : 'webgl'}`);

// ────────────────────────────────────────────────────────────────
// Auto-update: fetch version.json (written at CI publish time)
// with no-cache so the browser always hits the network. If the
// build hash changed since the last visit, flush all browser
// caches and reload — completely transparent to the user.
// version.json is absent in local dev builds, so errors are silently ignored.
// ────────────────────────────────────────────────────────────────
async function checkForUpdate() {
    try {
        const res = await fetch('./version.json', { cache: 'no-cache' });
        if (!res.ok) return;
        const { hash } = await res.json();
        const stored = localStorage.getItem('seg_buildHash');
        if (stored && stored !== hash) {
            // New version detected — flush all caches and force a fresh load.
            // Store the new hash first to avoid an infinite reload loop if
            // something goes wrong after the page comes back.
            localStorage.setItem('seg_buildHash', hash);
            if ('caches' in window) {
                const keys = await caches.keys();
                await Promise.all(keys.map(k => caches.delete(k)));
            }
            location.reload();
            return;
        }
        localStorage.setItem('seg_buildHash', hash);
    } catch {
        // version.json absent (local dev build) or network error — proceed normally.
    }
}
await checkForUpdate();

// ────────────────────────────────────────────────────────────────
// Canvas
// ────────────────────────────────────────────────────────────────
const gameCanvas = document.getElementById('gameCanvas');
let canvasWidth = 0;
let canvasHeight = 0;

function resizeCanvas() {
    const w = window.innerWidth;
    const h = window.innerHeight;
    gameCanvas.width = w;   // Use CSS pixels (not DPR-scaled) for 1:1 mapping with game coords
    gameCanvas.height = h;
    canvasWidth = w;
    canvasHeight = h;
}
resizeCanvas();
window.addEventListener('resize', resizeCanvas);

// ────────────────────────────────────────────────────────────────
// Renderer (created lazily on first use)
// ────────────────────────────────────────────────────────────────
let _renderer = null;
function getRenderer() {
    if (!_renderer) {
        _renderer = USE_CANVAS2D
            ? createCanvas2DRenderer(gameCanvas)
            : createWebGLRenderer(gameCanvas);
    }
    return _renderer;
}

// ────────────────────────────────────────────────────────────────
// Input state
// ────────────────────────────────────────────────────────────────
let mouseX = 0, mouseY = 0, mouseWheelAccum = 0;
const inputEvents = [];
let textInputBuffer = '';

gameCanvas.addEventListener('mousemove', e => { mouseX = e.offsetX; mouseY = e.offsetY; });
gameCanvas.addEventListener('mousedown', e => {
    inputEvents.push(`MD:${e.button}`);
    resumeAudio();
});
gameCanvas.addEventListener('mouseup', e => { inputEvents.push(`MU:${e.button}`); });
gameCanvas.addEventListener('wheel', e => { mouseWheelAccum -= e.deltaY / 120; e.preventDefault(); }, { passive: false });
gameCanvas.addEventListener('contextmenu', e => e.preventDefault());

// ── Gamepad state ──────────────────────────────────────────────
let gamepadConnected = false;
window.addEventListener('gamepadconnected', () => { gamepadConnected = true; });
window.addEventListener('gamepaddisconnected', () => {
    const gps = navigator.getGamepads();
    gamepadConnected = gps && Array.from(gps).some(g => g !== null);
});

document.addEventListener('keydown', e => {
    // Allow F12 for dev tools, F5 for refresh
    if (e.code === 'F12' || e.code === 'F5') return;

    inputEvents.push(`KD:${e.code}`);
    resumeAudio();

    // Text input handling
    if (e.key === 'Backspace') {
        textInputBuffer += '\b';
    } else if (e.key === 'Enter') {
        textInputBuffer += '\n';
    } else if (e.key.length === 1) {
        textInputBuffer += e.key;
    }

    e.preventDefault();
});
document.addEventListener('keyup', e => {
    if (e.code === 'F12' || e.code === 'F5') return;
    inputEvents.push(`KU:${e.code}`);
    e.preventDefault();
});

// ────────────────────────────────────────────────────────────────
// Audio state
// ────────────────────────────────────────────────────────────────
let audioCtx = null;
let audioNextTime = 0;
let audioResumed = false;

function resumeAudio() {
    if (audioCtx && audioCtx.state === 'suspended') {
        audioCtx.resume();
        audioResumed = true;
    }
}

// ────────────────────────────────────────────────────────────────
// Module imports for C# [JSImport]
// ────────────────────────────────────────────────────────────────
const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet
    .withDiagnosticTracing(false)
    .create();

document.getElementById('loadingStatus').textContent = 'Initializing runtime...';

setModuleImports('game.js', {
    // ── Canvas rendering ─────────────────────────────────────────
    canvas: {
        // ── Buffered command replay ──────────────────────────────
        // Decodes the entire RenderCommandBuffer binary payload in one JS call,
        // eliminating per-draw-call interop marshaling overhead.
        //
        // Binary layout uses little-endian IEEE-754 floats and Int32/Int64;
        // textures are stored as Int64 (low 32 bits = texture ID).
        flushCommandBuffer(buffer, length, cachedCircleTexId) {
            return getRenderer().flushCommandBuffer(buffer, length, cachedCircleTexId);
        },
    },

    // ── Texture management ───────────────────────────────────────
    texture: {
        create(pixels, width, height, scaleMode) {
            return getRenderer().textureCreate(pixels, width, height, scaleMode);
        },
        destroy(id) {
            getRenderer().textureDestroy(id);
        },
    },

    // ── Input ────────────────────────────────────────────────────
    input: {
        getMouseX() { return mouseX; },
        getMouseY() { return mouseY; },
        getMouseWheel() {
            const v = mouseWheelAccum;
            mouseWheelAccum = 0;
            return v;
        },
        flushEvents() {
            if (inputEvents.length === 0) return '';
            const result = inputEvents.join('|');
            inputEvents.length = 0;
            return result;
        },
        getCanvasWidth() { return canvasWidth; },
        getCanvasHeight() { return canvasHeight; },
        getTextInput() {
            const result = textInputBuffer;
            textInputBuffer = '';
            return result;
        },
        // Returns: "connected|b0,b1,...|a0,a1,..." or "" if no gamepad
        // b = button states (0/1), a = axis values (float)
        pollGamepad() {
            if (!gamepadConnected) return '';
            const gamepads = navigator.getGamepads();
            if (!gamepads) return '';
            let gp = null;
            for (let i = 0; i < gamepads.length; i++) {
                if (gamepads[i] && gamepads[i].connected) { gp = gamepads[i]; break; }
            }
            if (!gp) return '';
            resumeAudio();
            const btns = [];
            for (let i = 0; i < Math.min(gp.buttons.length, 17); i++) {
                btns.push(gp.buttons[i].pressed ? '1' : '0');
            }
            const axes = [];
            for (let i = 0; i < Math.min(gp.axes.length, 4); i++) {
                axes.push(gp.axes[i].toFixed(5));
            }
            return `1|${btns.join(',')}|${axes.join(',')}`;
        },
    },

    // ── Audio ────────────────────────────────────────────────────
    audio: {
        init(sampleRate) {
            try {
                audioCtx = new AudioContext({ sampleRate });
                audioNextTime = 0;
                return true;
            } catch (e) {
                console.warn('Web Audio init failed:', e);
                return false;
            }
        },

        pushChunk(buffer, frames) {
            if (!audioCtx || audioCtx.state === 'suspended') return;

            const audioBuf = audioCtx.createBuffer(2, frames, audioCtx.sampleRate);
            const left = audioBuf.getChannelData(0);
            const right = audioBuf.getChannelData(1);

            // buffer is a Float64Array view (double[]) — interleaved L/R
            for (let i = 0; i < frames; i++) {
                left[i] = buffer[i * 2];
                right[i] = buffer[i * 2 + 1];
            }

            const source = audioCtx.createBufferSource();
            source.buffer = audioBuf;
            source.connect(audioCtx.destination);

            const now = audioCtx.currentTime;
            if (audioNextTime < now) audioNextTime = now;
            source.start(audioNextTime);
            audioNextTime += frames / audioCtx.sampleRate;
        },

        getBufferedDuration() {
            if (!audioCtx) return 0;
            return Math.max(0, audioNextTime - audioCtx.currentTime);
        },
    },

    // ── Settings ─────────────────────────────────────────────────
    settings: {
        save(key, value) {
            try { localStorage.setItem('seg_' + key, value); } catch { }
        },
        load(key) {
            try { return localStorage.getItem('seg_' + key); } catch { return null; }
        },
    },

    // ── Launch options ────────────────────────────────────────────
    // Mirrors the SDL CLI argument parser: read named URL query parameters so
    // the web build can be launched with e.g. ?seed=42&location=planet&sublocation=on-foot
    launchOptions: {
        getUrlParam(name) {
            try {
                const params = new URLSearchParams(window.location.search);
                return params.get(name);
            } catch {
                return null;
            }
        },
    },
});

// ────────────────────────────────────────────────────────────────
// Boot the .NET runtime
// ────────────────────────────────────────────────────────────────
document.getElementById('loadingStatus').textContent = 'Loading assemblies...';

const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);

document.getElementById('loadingStatus').textContent = 'Starting game...';

// Run C# Main() to initialize the game
try {
    console.log('[SEG] Calling runMain()...');
    await runMain();
    console.log('[SEG] runMain() completed successfully');
} catch (e) {
    console.error('[SEG] runMain() failed:', e);
    document.getElementById('loadingStatus').textContent = 'Error: ' + e.message;
    throw e;
}

// Hide loading overlay
document.getElementById('loading').classList.add('hidden');
console.log('[SEG] Game loop starting...');

// Start the game loop
let frameCount = 0;
function gameLoop() {
    try {
        exports.SpaceExplorationGame.WebMain.RunOneFrame();
        frameCount++;
        if (frameCount <= 3) console.log(`[SEG] Frame ${frameCount} completed`);
    } catch (e) {
        console.error('[SEG] Game error in frame ' + frameCount + ':', e);
        document.getElementById('loading').classList.remove('hidden');
        document.getElementById('loadingStatus').textContent = 'Runtime error: ' + e.message;
        return; // Stop the loop on error
    }
    requestAnimationFrame(gameLoop);
}
requestAnimationFrame(gameLoop);
