import { AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, OnDestroy, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import * as signalR from '@microsoft/signalr';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { firstValueFrom } from 'rxjs';
import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import { environment } from '../../environments/environment';

/**
 * Interactive web terminal (exec) into a running container. Connects to DockiUpHub and bridges the
 * container's TTY into an xterm.js canvas (StartExec/WriteExec/ResizeExec -> ExecOutput/ExecExited).
 */
@Component({
  selector: 'app-container-terminal',
  imports: [MatIconModule, MatButtonModule, RouterLink],
  templateUrl: './container-terminal.html',
  styleUrl: './container-terminal.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ContainerTerminal implements AfterViewInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly oidc = inject(OidcSecurityService);
  protected readonly containerId = this.route.snapshot.paramMap.get('containerId') ?? '';
  // The node the container runs on (absent/empty = local control-plane host).
  private readonly nodeId = this.route.snapshot.queryParamMap.get('nodeId') || null;
  protected readonly status = signal<'connecting' | 'open' | 'closed'>('connecting');

  private readonly hostRef = viewChild<ElementRef<HTMLDivElement>>('term');

  private term: Terminal | null = null;
  private fit: FitAddon | null = null;
  private conn: signalR.HubConnection | null = null;
  private sessionId: string | null = null;
  private resizeObserver: ResizeObserver | null = null;

  ngAfterViewInit(): void {
    void this.start();
  }

  private async start(): Promise<void> {
    const host = this.hostRef()?.nativeElement;
    if (!host || !this.containerId) return;

    const term = new Terminal({
      cursorBlink: true,
      fontFamily: '"Roboto Mono", ui-monospace, SFMono-Regular, Menlo, Consolas, monospace',
      fontSize: 13,
      scrollback: 5000,
      convertEol: true,
      theme: { background: '#0a0a0a', foreground: '#e5e5e5', cursor: '#e5e5e5' },
    });
    const fit = new FitAddon();
    term.loadAddon(fit);
    term.open(host);
    queueMicrotask(() => { try { fit.fit(); } catch { /* not laid out yet */ } });
    this.term = term;
    this.fit = fit;

    this.resizeObserver = new ResizeObserver(() => {
      try { fit.fit(); } catch { /* ignore */ }
      if (this.sessionId && this.conn) {
        this.conn.invoke('ResizeExec', this.sessionId, term.cols, term.rows).catch(() => { });
      }
    });
    this.resizeObserver.observe(host);

    const base = (environment.apiBaseUrl ?? '').replace(/\/$/, '');
    const conn = new signalR.HubConnectionBuilder()
      // Bearer token for the hub when auth is enabled; empty in open mode (anonymous accepted).
      .withUrl(`${base}/hubs/dockiup`, {
        withCredentials: true,
        accessTokenFactory: async () => (await firstValueFrom(this.oidc.getAccessToken())) ?? '',
      })
      .build();
    this.conn = conn;

    conn.on('ExecOutput', (sessionId: string, base64: string) => {
      if (sessionId === this.sessionId) term.write(base64ToBytes(base64));
    });
    conn.on('ExecExited', (sessionId: string, code: number | null) => {
      if (sessionId !== this.sessionId) return;
      term.writeln(`\r\n\x1b[33m[exec exited: code ${code ?? '?'}]\x1b[0m`);
      this.status.set('closed');
    });

    try {
      await conn.start();
      const sessionId = await conn.invoke<string>('StartExec', this.containerId, term.cols, term.rows, this.nodeId);
      this.sessionId = sessionId;
      this.status.set('open');

      term.onData((data) => {
        if (!this.sessionId) return;
        conn.invoke('WriteExec', this.sessionId, bytesToBase64(new TextEncoder().encode(data))).catch(() => { });
      });
      term.onResize(({ cols, rows }) => {
        if (!this.sessionId) return;
        conn.invoke('ResizeExec', this.sessionId, cols, rows).catch(() => { });
      });
      term.focus();
    } catch {
      this.status.set('closed');
      term.writeln('\x1b[31m[failed to open exec session]\x1b[0m');
    }
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
    this.resizeObserver = null;
    if (this.sessionId && this.conn) {
      this.conn.invoke('EndExec', this.sessionId).catch(() => { });
    }
    try { this.term?.dispose(); } catch { /* ignore */ }
    this.term = null;
    void this.conn?.stop();
    this.conn = null;
  }
}

function bytesToBase64(bytes: Uint8Array): string {
  let s = '';
  for (let i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
  return btoa(s);
}

function base64ToBytes(base64: string): Uint8Array {
  const s = atob(base64);
  const bytes = new Uint8Array(s.length);
  for (let i = 0; i < s.length; i++) bytes[i] = s.charCodeAt(i);
  return bytes;
}
