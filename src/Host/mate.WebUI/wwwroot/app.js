// mate — WebUI JavaScript helpers
// v0.1.0

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
