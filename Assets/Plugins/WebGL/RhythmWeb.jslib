// WebGL-only helpers, called from C# through [DllImport("__Internal")].
// Needs Unity 2021.2 or newer (UTF8ToString). Unity ignores this file on other platforms.
mergeInto(LibraryManager.library, {
  RhythmDownloadText: function (filenamePtr, textPtr) {
    var filename = UTF8ToString(filenamePtr);
    var text = UTF8ToString(textPtr);
    var blob = new Blob([text], { type: 'application/json' });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
  }
});
