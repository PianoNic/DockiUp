import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class SignalRService {
  private hubConnection!: signalR.HubConnection;

  constructor() {
    this.startConnection();
  }

  private startConnection() {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(environment.apiBaseUrl + '/api/notificationHub')
      .build();

    this.hubConnection.start()
      .then(() => console.log('SignalR Connected!'))
      .catch(err => console.error('SignalR Connection Error:', err));
  }

  listenForContainerUpdates(callback: (containerId: string, status: string) => void) {
    this.hubConnection.on('ContainerUpdated', (data: { containerId: string, status: string }) => {
      callback(data.containerId, data.status);
    });
  }
}
