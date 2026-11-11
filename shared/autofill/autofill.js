// Blog Studio 자동 채우기 — 블로그 글쓰기 화면 안에서 실행되는 도우미.
// 앱(WebView2)이 이 스크립트를 넣고 window.__bs.* 를 호출한다.
// 클릭·붙여넣기·타이핑은 앱이 진짜 입력(DevTools Input 이벤트)으로 보내고,
// 이 스크립트는 "어디를 눌러야 하는지" 위치만 찾아 준다. 발행 버튼은 절대 누르지 않는다.
//
// 에디터 화면이 바뀌어 못 찾으면 아래 SELECTORS 만 고치면 된다 (앱 재빌드 없이 shared\autofill 수정 가능).
(() => {
  const SELECTORS = {
    tistory: {
      title: ['#post-title-inp', 'textarea.textarea_tit', 'textarea[placeholder*="제목"]', 'input[placeholder*="제목"]'],
      body: ['#editor-tistory_ifr', 'iframe[id$="_ifr"]', '.mce-content-body', '[contenteditable="true"]'],
      tags: ['#tagText', 'input[placeholder*="태그"]', 'input[name="tag"]'],
    },
    naver: {
      title: ['.se-documentTitle .se-text-paragraph', '.se-title-text .se-text-paragraph', '.se-documentTitle [contenteditable]'],
      body: ['.se-component.se-text .se-text-paragraph', '.se-section-text .se-text-paragraph', '.se-content .se-text-paragraph'],
      tags: [],
      // 닫아도 글 내용에 영향 없는 안내 패널만
      dismiss: ['.se-help-panel-close-button', 'button.se-help-close'],
      blocking: ['.se-popup-alert-confirm', '.se-popup-container .se-popup-alert'],
    },
  };

  const platform = () => (/naver\.com$/.test(location.hostname) ? 'naver' : 'tistory');

  // 같은 출처 iframe까지 모두 (네이버는 mainFrame, 티스토리 본문은 TinyMCE iframe 안)
  function docs(root = document, out = []) {
    out.push(root);
    for (const f of root.querySelectorAll('iframe')) {
      try { if (f.contentDocument) docs(f.contentDocument, out); } catch { /* 다른 출처 */ }
    }
    return out;
  }

  function visible(el) {
    const r = el.getBoundingClientRect();
    return r.width > 0 && r.height > 0;
  }

  function find(list) {
    for (const sel of list || []) {
      for (const d of docs()) {
        const el = [...d.querySelectorAll(sel)].find(visible);
        if (el) return el;
      }
    }
    return null;
  }

  // 요소 화면 좌표 (iframe 안이면 바깥 iframe 위치를 더한다) — 앱이 여기를 진짜로 클릭
  function point(el, where = 'center') {
    el.scrollIntoView({ block: 'center', inline: 'nearest' });
    const r = el.getBoundingClientRect();
    let x = where === 'end' ? r.right - 4 : r.left + Math.min(r.width / 2, 40);
    let y = r.top + r.height / 2;
    let w = el.ownerDocument.defaultView;
    while (w && w.frameElement) {
      const fr = w.frameElement.getBoundingClientRect();
      x += fr.left; y += fr.top;
      w = w.parent;
    }
    return { x: Math.round(x), y: Math.round(y) };
  }

  function tinymceEditor() {
    for (const d of docs()) {
      const t = d.defaultView && d.defaultView.tinymce;
      if (t && t.activeEditor) return t.activeEditor;
    }
    return null;
  }

  function placeholder(n) {
    const key = `[이미지 ${n}:`;
    for (const d of docs()) {
      const walker = d.createTreeWalker(d.body || d, NodeFilter.SHOW_TEXT);
      let node;
      while ((node = walker.nextNode())) {
        if (node.nodeValue.includes(key)) {
          return node.parentElement.closest('p, .se-text-paragraph, div') || node.parentElement;
        }
      }
    }
    return null;
  }

  function selectAll(el) {
    const d = el.ownerDocument;
    const editable = el.closest('[contenteditable="true"]') || el;
    if (editable.focus) editable.focus({ preventScroll: true });
    const range = d.createRange();
    range.selectNodeContents(el);
    const sel = d.defaultView.getSelection();
    sel.removeAllRanges();
    sel.addRange(range);
  }

  window.__bs = {
    version: 1,
    platform,

    state() {
      const p = platform();
      const s = SELECTORS[p];
      const host = location.hostname;
      const login = /nid\.naver\.com|accounts\.kakao\.com|\/auth\/login/.test(location.href);
      return {
        platform: p, url: location.href, login,
        title: !!find(s.title), body: !!(find(s.body) || tinymceEditor()),
        tags: !!find(s.tags), blocked: !!find(s.blocking), host,
      };
    },

    dismiss() {
      const list = SELECTORS[platform()].dismiss || [];
      let n = 0;
      if (list.length === 0) return 0;
      for (const d of docs()) {
        for (const el of d.querySelectorAll(list.join(','))) {
          if (visible(el)) { el.click(); n++; }
        }
      }
      return n;
    },

    titlePoint() {
      const el = find(SELECTORS[platform()].title);
      if (!el) return null;
      if (el.select) setTimeout(() => el.select(), 0);   // 티스토리 textarea: 기존 제목 덮어쓰기
      return point(el);
    },

    // 티스토리: TinyMCE에 HTML을 직접 넣는다 (붙여넣기 필터를 거치지 않아 표·코드 스타일 유지)
    setBodyHtml(html) {
      const ed = tinymceEditor();
      if (!ed) return false;
      ed.focus();
      ed.setContent(html);
      ed.undoManager && ed.undoManager.add();
      ed.fire && ed.fire('change');
      ed.save && ed.save();
      return true;
    },

    bodyPoint() {
      const s = SELECTORS[platform()];
      const ed = tinymceEditor();
      if (ed) {
        const b = ed.getBody();
        const last = b.lastElementChild || b;
        return point(last, 'end');
      }
      const el = find(s.body);
      return el ? point(el) : null;
    },

    hasPlaceholder(n) { return !!placeholder(n); },

    // 본문 안 이미지 수 (붙여넣은 이미지가 업로드돼 들어왔는지 확인용)
    imageCount() {
      const ed = tinymceEditor();
      if (ed) return ed.getBody().querySelectorAll('img').length;
      return docs().reduce((sum, d) => sum + d.querySelectorAll('.se-component.se-image img, .se-image-resource').length, 0);
    },

    // [이미지 N] 안내 줄 지우기. TinyMCE(티스토리)는 편집기 기능으로 바로 지우고 'done',
    // 그 밖의 편집기(네이버)는 줄을 선택해 두고 'select' → 앱이 Backspace 를 보낸다.
    removePlaceholder(n) {
      const el = placeholder(n);
      if (!el) return 'none';
      const ed = tinymceEditor();
      if (ed && el.ownerDocument === ed.getDoc()) {
        ed.undoManager && ed.undoManager.transact(() => ed.dom.remove(el));
        ed.fire && ed.fire('change');
        ed.save && ed.save();
        return 'done';
      }
      selectAll(el);
      return 'select';
    },

    // [이미지 N] 줄을 선택해 두고 위치를 돌려준다 → 앱이 그 위에서 이미지 붙여넣기
    selectPlaceholder(n) {
      const el = placeholder(n);
      if (!el) return null;
      const pt = point(el);
      const ed = tinymceEditor();
      if (ed && el.ownerDocument === ed.getDoc()) {
        ed.focus();
        ed.selection.select(el, true);
      } else {
        selectAll(el);
      }
      return pt;
    },

    tagsPoint() {
      const el = find(SELECTORS[platform()].tags);
      return el ? point(el) : null;
    },
  };
  return true;
})();
