// mate — WebUI JavaScript helpers
// v0.1.0

// ── Dark mode ────────────────────────────────────────────────────────────────

window.mateInitDarkMode = function () {
    const saved = localStorage.getItem('mate-theme');
    if (saved === 'dark') {
        document.documentElement.setAttribute('data-theme', 'dark');
    } else {
        document.documentElement.removeAttribute('data-theme');
    }
    return saved === 'dark';
};

window.mateSetDarkMode = function (enabled) {
    if (enabled) {
        document.documentElement.setAttribute('data-theme', 'dark');
        localStorage.setItem('mate-theme', 'dark');
    } else {
        document.documentElement.removeAttribute('data-theme');
        localStorage.setItem('mate-theme', 'light');
    }
};

window.mateGetDarkMode = function () {
    return document.documentElement.getAttribute('data-theme') === 'dark';
};

// Auto-initialise from localStorage before first render
(function () { window.mateInitDarkMode(); })();

window.downloadCsvFromBase64 = function (base64, fileName) {
    const a = document.createElement('a');
    a.href = 'data:text/csv;base64,' + base64;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
};

window.downloadTextFile = function (text, fileName, mimeType) {
    const blob = new Blob([text], { type: mimeType || 'text/plain' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(a.href);
};

window.downloadFileFromBase64 = function (base64, fileName, mimeType) {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }

    const blob = new Blob([bytes], { type: mimeType || 'application/octet-stream' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(a.href);
};

window.copyToClipboard = function (text) {
    return navigator.clipboard.writeText(text).then(() => true).catch(() => false);
};

window.scrollToTop = function () {
    window.scrollTo({ top: 0, behavior: 'smooth' });
};

window.focusElement = function (elementId) {
    const el = document.getElementById(elementId);
    if (el) el.focus();
};

window.mateIsMobile = function () {
    return window.matchMedia('(max-width: 768px)').matches;
};

window.triggerUrlDownload = function (url) {
    const a = document.createElement('a');
    a.href = url;
    a.style.display = 'none';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
};
