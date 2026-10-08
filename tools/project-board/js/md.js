// A very small Markdown subset for card bodies: headings, lists, quotes, fenced
// and inline code, bold, italic and links. No dependency, because a card body is
// a paragraph and a bullet list and a path in backticks, and that is all.
//
// Everything is escaped before any rule runs, and code spans are lifted out
// before the inline rules so a path like `a*b*c` is not silently italicised.
(function (PB) {
  "use strict";

  function escapeHtml(s) {
    return String(s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;")
      .replace(/'/g, "&#39;");
  }

  // Anything that is not plainly a web or mail address is rendered as text, so a
  // javascript: or data: URL in a card body can never become a live link.
  function safeUrl(url) {
    return /^(https?:\/\/|mailto:)/i.test(url) ? url : null;
  }

  function inline(text) {
    let out = text;
    out = out.replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, (m, label, url) => {
      const safe = safeUrl(url);
      return safe ? `<a href="${safe}" target="_blank" rel="noopener noreferrer">${label}</a>` : m;
    });
    out = out.replace(/(^|[\s(])(https?:\/\/[^\s<)]+)/g, (m, pre, url) =>
      `${pre}<a href="${url}" target="_blank" rel="noopener noreferrer">${url}</a>`);
    out = out.replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");
    out = out.replace(/(^|[^*])\*([^*\n]+)\*/g, "$1<em>$2</em>");
    out = out.replace(/(^|[^_\w])_([^_\n]+)_/g, "$1<em>$2</em>");
    return out;
  }

  PB.escapeHtml = escapeHtml;

  PB.markdown = function (src) {
    if (!src) return "";
    const codes = [];
    const stash = html => {
      codes.push(html);
      return `\u0000${codes.length - 1}\u0000`;
    };

    let text = escapeHtml(src).replace(/\r\n?/g, "\n");
    text = text.replace(/```([\s\S]*?)```/g, (m, body) => stash(`<pre><code>${body.replace(/^\n/, "")}</code></pre>`));
    text = text.replace(/`([^`\n]+)`/g, (m, body) => stash(`<code>${body}</code>`));

    const lines = text.split("\n");
    const out = [];
    let list = null; // "ul" | "ol" | null
    let para = [];
    let quote = [];

    const flushPara = () => {
      if (para.length) { out.push(`<p>${inline(para.join(" "))}</p>`); para = []; }
    };
    const flushList = () => {
      if (list) { out.push(`</${list}>`); list = null; }
    };
    const flushQuote = () => {
      if (quote.length) { out.push(`<blockquote>${inline(quote.join(" "))}</blockquote>`); quote = []; }
    };
    const flushAll = () => { flushPara(); flushList(); flushQuote(); };

    for (const raw of lines) {
      const line = raw.trimEnd();

      if (/^\u0000\d+\u0000$/.test(line.trim())) { flushAll(); out.push(line.trim()); continue; }
      if (!line.trim()) { flushAll(); continue; }

      const heading = line.match(/^(#{1,4})\s+(.*)$/);
      if (heading) {
        flushAll();
        const level = Math.min(6, heading[1].length + 2);
        out.push(`<h${level}>${inline(heading[2])}</h${level}>`);
        continue;
      }

      const bullet = line.match(/^\s*[-*+]\s+(.*)$/);
      if (bullet) {
        flushPara(); flushQuote();
        if (list !== "ul") { flushList(); out.push("<ul>"); list = "ul"; }
        out.push(`<li>${inline(bullet[1])}</li>`);
        continue;
      }

      const numbered = line.match(/^\s*\d+[.)]\s+(.*)$/);
      if (numbered) {
        flushPara(); flushQuote();
        if (list !== "ol") { flushList(); out.push("<ol>"); list = "ol"; }
        out.push(`<li>${inline(numbered[1])}</li>`);
        continue;
      }

      const quoted = line.match(/^\s*&gt;\s?(.*)$/);
      if (quoted) { flushPara(); flushList(); quote.push(quoted[1]); continue; }

      flushList(); flushQuote();
      para.push(line.trim());
    }
    flushAll();

    return out.join("\n").replace(/\u0000(\d+)\u0000/g, (m, i) => codes[Number(i)]);
  };
})(window.PB = window.PB || {});
