"""마크다운 → 블로그 붙여넣기용 HTML(인라인 스타일) + 미리보기 페이지."""
from __future__ import annotations

import html
import json
import re

import markdown

# 네이버/티스토리 에디터는 <style> 태그를 버리므로 모든 스타일을 태그에 직접 넣는다.
STYLES = {
    "h2": "font-size:22px;font-weight:700;margin:36px 0 14px;padding-bottom:6px;border-bottom:2px solid #222;",
    "h3": "font-size:18px;font-weight:700;margin:26px 0 10px;",
    "h4": "font-size:16px;font-weight:700;margin:20px 0 8px;",
    "p": "font-size:16px;line-height:1.8;margin:0 0 14px;",
    "li": "font-size:16px;line-height:1.8;margin:2px 0;",
    "ul": "margin:0 0 14px;padding-left:22px;",
    "ol": "margin:0 0 14px;padding-left:22px;",
    "blockquote": "margin:16px 0;padding:10px 16px;border-left:4px solid #3b82f6;background:#f3f7ff;color:#333;",
    "table": "border-collapse:collapse;margin:14px 0;font-size:15px;",
    "th": "border:1px solid #ccc;background:#f5f5f5;padding:8px 12px;text-align:left;",
    "td": "border:1px solid #ccc;padding:8px 12px;",
    "hr": "border:none;border-top:1px solid #ddd;margin:28px 0;",
}
INLINE_CODE = "background:#f1f1f1;color:#c7254e;padding:2px 5px;border-radius:4px;font-family:Consolas,monospace;font-size:14px;"
CODE_BLOCK = (
    "background:#f8f8f8;border:1px solid #e5e5e5;border-radius:6px;padding:14px 16px;overflow-x:auto;"
    "font-family:Consolas,'D2Coding',monospace;font-size:14px;line-height:1.6;margin:0 0 16px;"
)


def _tasks(md_text: str) -> str:
    md_text = re.sub(r"^(\s*)[-*] \[ \] ", r"\1- ☐ ", md_text, flags=re.M)
    return re.sub(r"^(\s*)[-*] \[[xX]\] ", r"\1- ☑ ", md_text, flags=re.M)


def md_to_html(md_text: str) -> str:
    body = markdown.markdown(
        _tasks(md_text),
        extensions=["fenced_code", "tables", "sane_lists", "codehilite"],
        extension_configs={"codehilite": {"noclasses": True, "pygments_style": "friendly", "guess_lang": False}},
    )
    # 인라인 코드 (코드 블록 내부의 <code>는 pre 처리에서 덮어씀)
    body = body.replace("<code>", f'<code style="{INLINE_CODE}">')
    # 코드 블록: codehilite가 만든 div/pre에 스타일 부여
    body = re.sub(r'<div class="codehilite"[^>]*>', '<div class="codehilite">', body)
    body = re.sub(r"<pre[^>]*>", f'<pre style="{CODE_BLOCK}">', body)
    body = re.sub(r'(<pre[^>]*>)\s*(?:<span></span>)?<code style="[^"]*">', r"\1<code>", body)
    for tag, style in STYLES.items():
        body = re.sub(rf"<{tag}(\s[^>]*)?>", lambda m, t=tag, s=style: f'<{t} style="{s}"{m.group(1) or ""}>', body)
    return body


def build_preview(post, blog_cfg: dict, md_full: str, html_body: str) -> str:
    naver_id = (blog_cfg or {}).get("naver_id") or ""
    tistory = (blog_cfg or {}).get("tistory_name") or ""
    naver_url = f"https://blog.naver.com/{naver_id}?Redirect=Write" if naver_id else ""
    tistory_url = f"https://{tistory}.tistory.com/manage/newpost" if tistory else ""
    tags_txt = ", ".join(post.tags)
    data = json.dumps({"md": md_full, "title": post.title, "tags": tags_txt, "naverUrl": naver_url,
                       "tistoryUrl": tistory_url}, ensure_ascii=False).replace("</", "<\\/")

    def btn(label, js, primary=False):
        cls = "btn primary" if primary else "btn"
        return f'<button class="{cls}" onclick="{js}">{label}</button>'

    naver_btns = btn("네이버용 본문 복사" + (" + 글쓰기 열기" if naver_url else ""), "copyHtml('naver')", True)
    tistory_btns = btn("티스토리용 본문 복사" + (" + 글쓰기 열기" if tistory_url else ""), "copyHtml('tistory')", True) + \
        btn("마크다운 복사 (티스토리 마크다운 모드)", "copyText(D.md,'마크다운')")

    return f"""<!DOCTYPE html>
<html lang="ko"><head><meta charset="utf-8"><title>{html.escape(post.title)}</title>
<meta name="viewport" content="width=device-width,initial-scale=1">
<style>
 body{{margin:0;background:#eef0f3;font-family:-apple-system,'Malgun Gothic','Apple SD Gothic Neo',sans-serif;color:#111}}
 .bar{{position:sticky;top:0;z-index:5;background:#fff;border-bottom:1px solid #ddd;padding:14px 20px}}
 .bar .meta{{font-size:13px;color:#666;margin-bottom:8px}}
 .bar .title{{font-size:20px;font-weight:700;margin-bottom:10px}}
 .row{{display:flex;flex-wrap:wrap;gap:8px;align-items:center;margin-top:6px}}
 .lbl{{font-size:13px;color:#555;width:64px}}
 .btn{{border:1px solid #bbb;background:#fff;border-radius:6px;padding:7px 12px;font-size:13px;cursor:pointer}}
 .btn:hover{{background:#f3f3f3}} .btn.primary{{background:#111;color:#fff;border-color:#111}}
 .paper{{max-width:780px;margin:24px auto;background:#fff;padding:40px 48px;border-radius:8px;box-shadow:0 1px 4px rgba(0,0,0,.08)}}
 #toast{{position:fixed;bottom:24px;left:50%;transform:translateX(-50%);background:#111;color:#fff;padding:10px 18px;border-radius:20px;font-size:14px;opacity:0;transition:.2s}}
 .tag{{display:inline-block;background:#f0f0f0;border-radius:12px;padding:2px 10px;margin:2px;font-size:13px}}
</style></head><body>
<div class="bar">
  <div class="meta">{html.escape(post.track.name)} · 주제: {html.escape(post.topic)}</div>
  <div class="title">{html.escape(post.title)}</div>
  <div class="row"><span class="lbl">공통</span>{btn("제목 복사", "copyText(D.title,'제목')")}{btn("태그 복사", "copyText(D.tags,'태그')")}
    <span>{''.join(f'<span class="tag">#{html.escape(t)}</span>' for t in post.tags)}</span></div>
  <div class="row"><span class="lbl">네이버</span>{naver_btns}</div>
  <div class="row"><span class="lbl">티스토리</span>{tistory_btns}</div>
</div>
<div class="paper"><div id="content">{html_body}</div></div>
<div id="toast"></div>
<script>
const D = {data};
function toast(m){{const t=document.getElementById('toast');t.textContent=m;t.style.opacity=1;setTimeout(()=>t.style.opacity=0,1800);}}
async function copyText(s,label){{
  try{{await navigator.clipboard.writeText(s);}}catch(e){{const ta=document.createElement('textarea');ta.value=s;document.body.appendChild(ta);ta.select();document.execCommand('copy');ta.remove();}}
  toast(label+' 복사됨');
}}
async function copyHtml(target){{
  const el=document.getElementById('content');
  let ok=false;
  try{{
    await navigator.clipboard.write([new ClipboardItem({{'text/html':new Blob([el.innerHTML],{{type:'text/html'}}),'text/plain':new Blob([el.innerText],{{type:'text/plain'}})}})]);
    ok=true;
  }}catch(e){{}}
  if(!ok){{const r=document.createRange();r.selectNodeContents(el);const s=getSelection();s.removeAllRanges();s.addRange(r);ok=document.execCommand('copy');s.removeAllRanges();}}
  toast(ok?'본문 복사됨 — 글쓰기 화면 본문에 Ctrl+V':'복사 실패: 본문을 드래그해서 직접 복사해 주세요');
  const url = target==='naver'?D.naverUrl:D.tistoryUrl;
  if(ok&&url) setTimeout(()=>window.open(url,'_blank'),400);
}}
</script></body></html>"""
