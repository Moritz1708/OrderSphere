// Motion helpers for the Blazor client. Everything visual lives in motion.css;
// this module only observes, measures and toggles attributes.
// Loaded lazily as an ES module by Services/MotionService.cs.

const REDUCED_MOTION = '(prefers-reduced-motion: reduce)';

let observer = null;
let reducedQuery = null;

function prefersReduced() {
    return window.matchMedia(REDUCED_MOTION).matches;
}

function ensureObserver() {
    if (observer) return observer;

    observer = new IntersectionObserver(
        (entries) => {
            for (const entry of entries) {
                if (!entry.isIntersecting) continue;
                entry.target.classList.add('is-revealed');
                observer.unobserve(entry.target); // reveals are one-shot
            }
        },
        { rootMargin: '0px 0px -10% 0px', threshold: 0.1 }
    );

    return observer;
}

/** Marks the document ready for motion and keeps the reduced-motion flag current. */
export function init() {
    const root = document.documentElement;

    const sync = () => {
        const reduced = prefersReduced();
        if (reduced) {
            root.dataset.reducedMotion = 'true';
            // Anything already waiting must not stay hidden.
            document.querySelectorAll('[data-reveal]:not(.is-revealed)')
                .forEach((el) => el.classList.add('is-revealed'));
        } else {
            delete root.dataset.reducedMotion;
        }
    };

    if (!reducedQuery) {
        reducedQuery = window.matchMedia(REDUCED_MOTION);
        reducedQuery.addEventListener('change', sync);
    }

    sync();
    measureScrollbar();
    root.dataset.motion = 'ready';
}

/**
 * Reveals `root` on scroll. A [data-reveal-group] root instead indexes its
 * direct children so they stagger; a plain element reveals on its own.
 */
export function observeReveals(root) {
    if (!root) return;

    const targets = root.hasAttribute('data-reveal-group')
        ? Array.from(root.children)
        : [root];

    targets.forEach((el, i) => {
        if (root.hasAttribute('data-reveal-group')) {
            el.setAttribute('data-reveal', '');
            el.style.setProperty('--reveal-index', String(i));
        }

        if (prefersReduced()) {
            el.classList.add('is-revealed');
            return;
        }

        ensureObserver().observe(el);
    });
}

export function unobserve(root) {
    if (!root || !observer) return;

    const targets = root.hasAttribute('data-reveal-group')
        ? Array.from(root.children)
        : [root];

    targets.forEach((el) => observer.unobserve(el));
}

/** Applies a theme choice: attribute for CSS, localStorage for the next boot. */
export function setTheme(dark) {
    document.documentElement.dataset.theme = dark ? 'dark' : 'light';

    const meta = document.querySelector('meta[name="theme-color"]');
    if (meta) meta.setAttribute('content', dark ? '#0F0E0C' : '#F7F5F0');

    try {
        window.localStorage.setItem('os-dark-mode', dark ? 'true' : 'false');
    } catch (e) {
        /* storage disabled — the choice lasts for this session only */
    }
}

/** For non-Mud overlays. Mud's own drawers and dialogs lock the body themselves. */
export function lockScroll(locked) {
    document.body.classList.toggle('scroll-locked', !!locked);
}

export function prefersReducedMotion() {
    return prefersReduced();
}

/** Keeps the scroll-lock gutter honest instead of Mud's hard-coded 8px. */
function measureScrollbar() {
    const width = window.innerWidth - document.documentElement.clientWidth;
    document.documentElement.style.setProperty('--os-scrollbar-w', `${Math.max(0, width)}px`);
}
