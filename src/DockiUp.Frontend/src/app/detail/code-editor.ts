import { ChangeDetectionStrategy, Component, computed, ElementRef, input, model, output, viewChild } from '@angular/core';

export type EditorLanguage = 'yaml' | 'env' | 'plain';

/** Picks the highlighter from a file name: compose/YAML files, .env files, or none. */
export function languageFor(path: string): EditorLanguage {
  const name = path.split('/').pop()?.toLowerCase() ?? '';
  if (name.endsWith('.yml') || name.endsWith('.yaml')) return 'yaml';
  if (name === '.env' || name.startsWith('.env.') || name.endsWith('.env')) return 'env';
  return 'plain';
}

/**
 * A small code editor: a plain textarea (native undo, selection, IME) over a highlighted copy of the same
 * text, plus line numbers. Deliberately not CodeMirror/Monaco - those would multiply the bundle for what is
 * mostly editing compose and .env files.
 */
@Component({
  selector: 'app-code-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pre class="gutter" #gutter aria-hidden="true">{{ lineNumbers() }}</pre>
    <div class="area">
      <pre class="layer" #layer aria-hidden="true" [innerHTML]="highlighted()"></pre>
      <textarea #text class="layer" wrap="off" spellcheck="false" autocapitalize="off" [attr.aria-label]="label()"
        [value]="value()" (input)="value.set(text.value)" (scroll)="sync()" (keydown)="onKey($event)"></textarea>
    </div>
  `,
  styles: `
    :host { display: flex; min-height: 0; overflow: hidden; border-radius: 20px;
      background: var(--mat-sys-surface-container-lowest); font: 13px/1.6 var(--x-mono); }
    pre, textarea { margin: 0; padding: 14px; font: inherit; tab-size: 2; white-space: pre; box-sizing: border-box; }
    .gutter { flex-shrink: 0; min-width: 3.5em; overflow: hidden; text-align: right; color: var(--mat-sys-outline);
      background: var(--mat-sys-surface-container-low); user-select: none; }
    .area { position: relative; flex: 1; min-width: 0; }
    .layer { position: absolute; inset: 0; width: 100%; height: 100%; overflow: auto; }
    pre.layer { overflow: hidden; color: var(--mat-sys-on-surface); pointer-events: none; }
    textarea { border: 0; outline: 0; resize: none; background: transparent; color: transparent; caret-color: var(--mat-sys-on-surface); }
    textarea::selection { background: color-mix(in srgb, var(--mat-sys-primary) 30%, transparent); }
    :host ::ng-deep .t-k { color: var(--mat-sys-primary); }
    :host ::ng-deep .t-s { color: var(--mat-sys-tertiary); }
    :host ::ng-deep .t-c { color: var(--mat-sys-outline); font-style: italic; }
  `,
})
export class CodeEditor {
  readonly value = model.required<string>();
  readonly language = input<EditorLanguage>('plain');
  readonly label = input('File content');
  /** Ctrl/Cmd+S. */
  readonly save = output<void>();

  private readonly gutter = viewChild.required<ElementRef<HTMLElement>>('gutter');
  private readonly layer = viewChild.required<ElementRef<HTMLElement>>('layer');
  private readonly text = viewChild.required<ElementRef<HTMLTextAreaElement>>('text');

  protected readonly lineNumbers = computed(() => {
    const count = this.value().split('\n').length;
    return Array.from({ length: count }, (_, i) => i + 1).join('\n');
  });

  // The trailing newline keeps the layer as tall as the textarea when the text ends in an empty line.
  protected readonly highlighted = computed(() => highlight(this.value(), this.language()) + '\n ');

  protected sync(): void {
    const { scrollTop, scrollLeft } = this.text().nativeElement;
    this.layer().nativeElement.scrollTop = scrollTop;
    this.layer().nativeElement.scrollLeft = scrollLeft;
    this.gutter().nativeElement.scrollTop = scrollTop;
  }

  protected onKey(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') {
      event.preventDefault();
      this.save.emit();
    } else if (event.key === 'Tab' && !event.shiftKey && !event.ctrlKey && !event.altKey) {
      // YAML is indented with spaces; keep focus in the editor. execCommand keeps native undo working.
      event.preventDefault();
      document.execCommand('insertText', false, '  ');
    }
  }
}

const escapeHtml = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const span = (cls: string, s: string) => (s ? `<span class="t-${cls}">${escapeHtml(s)}</span>` : '');

/** A value: quoted strings, then an optional trailing " # comment". */
function valueHtml(v: string): string {
  const comment = v.search(/(^|\s)#/);
  const body = comment >= 0 ? v.slice(0, comment) : v;
  const tail = comment >= 0 ? v.slice(comment) : '';
  const quoted = /^(\s*)("[^"]*"?|'[^']*'?)(.*)$/.exec(body);
  const bodyHtml = quoted ? escapeHtml(quoted[1]) + span('s', quoted[2]) + escapeHtml(quoted[3]) : escapeHtml(body);
  return bodyHtml + span('c', tail);
}

function highlightLine(line: string, lang: EditorLanguage): string {
  const comment = /^(\s*)(#.*)$/.exec(line);
  if (comment) return escapeHtml(comment[1]) + span('c', comment[2]);
  const kv =
    lang === 'env'
      ? /^(\s*(?:export\s+)?)([\w.-]+)(=)(.*)$/.exec(line)
      : /^(\s*(?:-\s+)?)("[^"]*"|'[^']*'|[^\s#'"][^:#]*?)(:)((?:\s.*)?)$/.exec(line);
  if (kv) return escapeHtml(kv[1]) + span('k', kv[2]) + escapeHtml(kv[3]) + valueHtml(kv[4]);
  const item = /^(\s*-\s+)(.*)$/.exec(line);
  return item ? escapeHtml(item[1]) + valueHtml(item[2]) : valueHtml(line);
}

function highlight(text: string, lang: EditorLanguage): string {
  return lang === 'plain' ? escapeHtml(text) : text.split('\n').map((l) => highlightLine(l, lang)).join('\n');
}
