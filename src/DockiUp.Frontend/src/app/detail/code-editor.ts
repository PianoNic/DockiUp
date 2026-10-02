import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, effect, inject, input, model, output, viewChild } from '@angular/core';
import type { Extension, Transaction as TransactionType } from '@codemirror/state';
import type { EditorView } from '@codemirror/view';

export type EditorLanguage = 'compose' | 'yaml' | 'env' | 'json' | 'python' | 'javascript' | 'dockerfile' | 'shell' | 'plain';

/** Picks the language from a file name. Compose files get schema-aware completion, hover help and linting. */
export function languageFor(path: string): EditorLanguage {
  const name = path.split('/').pop()?.toLowerCase() ?? '';
  if (/^(docker-)?compose(\.[\w.-]+)?\.ya?ml$/.test(name)) return 'compose';
  if (name.endsWith('.yml') || name.endsWith('.yaml')) return 'yaml';
  if (name === '.env' || name.startsWith('.env.') || name.endsWith('.env')) return 'env';
  if (name.endsWith('.json')) return 'json';
  if (name.endsWith('.py')) return 'python';
  if (/\.(m?[jt]sx?|cjs)$/.test(name)) return 'javascript';
  if (name === 'dockerfile' || name.startsWith('dockerfile.') || name.endsWith('.dockerfile')) return 'dockerfile';
  if (/\.(sh|bash|zsh)$/.test(name)) return 'shell';
  return 'plain';
}

/**
 * CodeMirror 6, loaded on first use (its own chunk, so it only costs something once the Files tab opens).
 * Compose files are checked against the official compose-spec schema (public/compose-spec.json, self-hosted):
 * completion of keys, hover descriptions and red underlines for mistakes.
 */
@Component({
  selector: 'app-code-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div class="host" #host></div>`,
  styles: `
    :host { display: flex; min-height: 0; overflow: hidden; border-radius: 28px; background: #1d2433; }
    .host { flex: 1; min-width: 0; min-height: 0; display: flex; }
  `,
})
export class CodeEditor {
  readonly value = model.required<string>();
  readonly language = input<EditorLanguage>('plain');
  readonly label = input('File content');
  /** Ctrl/Cmd+S. */
  readonly save = output<void>();

  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  private view?: EditorView;
  private reconfigure?: (language: EditorLanguage) => Promise<void>;
  private setSeparator?: (text: string) => void;
  private notInHistory?: ReturnType<typeof TransactionType.addToHistory.of>;

  constructor() {
    const destroyRef = inject(DestroyRef);
    void this.create().then(() => destroyRef.onDestroy(() => this.view?.destroy()));

    // A newly opened file (or a reload) replaces the document; typing doesn't come back through here.
    effect(() => {
      const value = this.value();
      const view = this.view;
      if (view && view.state.sliceDoc() !== value) {
        this.setSeparator?.(value);
        // Loading a file is not an edit: keep it out of the undo history.
        view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: value }, annotations: this.notInHistory });
      }
    });
    effect(() => {
      const language = this.language();
      void this.reconfigure?.(language);
    });
  }

  private async create(): Promise<void> {
    const [{ basicSetup }, { EditorState, Compartment, Transaction }, { EditorView, keymap }, { indentWithTab }, { syntaxHighlighting, HighlightStyle, indentUnit }, { tags }] =
      await Promise.all([
        import('codemirror'),
        import('@codemirror/state'),
        import('@codemirror/view'),
        import('@codemirror/commands'),
        import('@codemirror/language'),
        import('@lezer/highlight'),
      ]);

    this.notInHistory = Transaction.addToHistory.of(false);
    const languageSlot = new Compartment();
    // Keep each file's own line endings: CodeMirror would otherwise write a CRLF file back as LF on the first edit.
    const separatorSlot = new Compartment();
    const separatorFor = (text: string) => EditorState.lineSeparator.of(text.includes('\r\n') ? '\r\n' : '\n');
    const highlight = HighlightStyle.define([
      { tag: [tags.keyword, tags.propertyName, tags.definition(tags.propertyName)], color: '#8ab4f8' },
      { tag: [tags.string, tags.special(tags.string)], color: '#c3e88d' },
      { tag: [tags.number, tags.bool, tags.null, tags.atom], color: '#ffcb8b' },
      { tag: [tags.comment, tags.lineComment, tags.blockComment], color: '#8a94a8', fontStyle: 'italic' },
      { tag: [tags.variableName, tags.attributeName], color: '#dbe2f9' },
      { tag: [tags.function(tags.variableName), tags.typeName, tags.className], color: '#ffd8e7' },
      { tag: [tags.operator, tags.punctuation, tags.separator], color: '#aab4cc' },
      { tag: tags.invalid, color: '#ffb4ab' },
    ]);
    // The dark code surface used for logs: Roboto Mono, soft gutter, rounded tooltips that match the app's menus.
    const theme = EditorView.theme(
      {
        '&': { height: '100%', width: '100%', color: '#dbe2f9', backgroundColor: '#1d2433', fontSize: '13px' },
        '.cm-scroller': { fontFamily: 'var(--x-mono)', lineHeight: '1.6' },
        '.cm-content': { padding: '14px 0', caretColor: '#dbe2f9' },
        '.cm-gutters': { backgroundColor: '#1d2433', color: '#5f6b84', border: 'none', paddingLeft: '8px' },
        '.cm-activeLine, .cm-activeLineGutter': { backgroundColor: 'rgba(219, 226, 249, 0.05)' },
        '.cm-cursor': { borderLeftColor: '#dbe2f9' },
        '&.cm-focused .cm-selectionBackground, .cm-selectionBackground, ::selection': { backgroundColor: 'rgba(138, 180, 248, 0.28) !important' },
        '.cm-matchingBracket': { backgroundColor: 'rgba(138, 180, 248, 0.25)', outline: 'none' },
        '.cm-searchMatch': { backgroundColor: 'rgba(255, 203, 139, 0.25)' },
        '.cm-panels': { backgroundColor: '#262e40', color: '#dbe2f9', borderTop: 'none' },
        '.cm-panels input, .cm-panels button': { fontFamily: 'inherit' },
        '.cm-tooltip': { border: 'none', borderRadius: '14px', backgroundColor: '#262e40', color: '#dbe2f9', overflow: 'hidden', boxShadow: '0 8px 24px rgba(0,0,0,.35)' },
        '.cm-tooltip-autocomplete > ul > li[aria-selected]': { backgroundColor: 'rgba(138, 180, 248, 0.25)', color: '#fff' },
        '.cm-tooltip-hover, .cm-tooltip-lint': { padding: '8px 12px', maxWidth: '420px', fontFamily: 'var(--x-mono)' },
        '.cm-foldPlaceholder': { backgroundColor: '#262e40', border: 'none', color: '#aab4cc' },
      },
      { dark: true },
    );

    this.view = new EditorView({
      parent: this.host().nativeElement,
      state: EditorState.create({
        doc: this.value(),
        extensions: [
          basicSetup,
          keymap.of([
            { key: 'Mod-s', preventDefault: true, run: () => { this.save.emit(); return true; } },
            indentWithTab,
          ]),
          indentUnit.of('  '),
          separatorSlot.of(separatorFor(this.value())),
          theme,
          syntaxHighlighting(highlight),
          languageSlot.of(await languageExtension(this.language())),
          EditorView.contentAttributes.of({ 'aria-label': this.label() }),
          EditorView.updateListener.of((update) => {
            if (update.docChanged) this.value.set(update.state.sliceDoc()); // sliceDoc honours the line separator; doc.toString() is always LF
          }),
        ],
      }),
    });
    this.setSeparator = (text) => this.view?.dispatch({ effects: separatorSlot.reconfigure(separatorFor(text)) });
    this.reconfigure = async (language) => {
      this.view?.dispatch({ effects: languageSlot.reconfigure(await languageExtension(language)) });
    };
  }
}

let composeSchema: Promise<object> | undefined;

async function languageExtension(language: EditorLanguage): Promise<Extension> {
  const legacy = async (mode: Promise<{ [name: string]: unknown }>, name: string) => {
    const { StreamLanguage } = await import('@codemirror/language');
    return StreamLanguage.define((await mode)[name] as never);
  };
  switch (language) {
    case 'compose': {
      const [{ yamlSchema }, schema] = await Promise.all([
        import('codemirror-json-schema/yaml'),
        (composeSchema ??= fetch('compose-spec.json').then((r) => r.json())),
      ]);
      return yamlSchema(schema as never);
    }
    case 'yaml': return (await import('@codemirror/lang-yaml')).yaml();
    case 'json': return (await import('@codemirror/lang-json')).json();
    case 'python': return (await import('@codemirror/lang-python')).python();
    case 'javascript': return (await import('@codemirror/lang-javascript')).javascript({ typescript: true });
    case 'env': return legacy(import('@codemirror/legacy-modes/mode/properties'), 'properties');
    case 'dockerfile': return legacy(import('@codemirror/legacy-modes/mode/dockerfile'), 'dockerFile');
    case 'shell': return legacy(import('@codemirror/legacy-modes/mode/shell'), 'shell');
    default: return [];
  }
}
