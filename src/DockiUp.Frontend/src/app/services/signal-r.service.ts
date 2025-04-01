import { Injectable } from '@angular/core';
import { environment } from '../../environments/environment';
import { ContainerDto } from '../api';
import * as signalR from '@microsoft/signalr';

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
      .then(() => console.log('NotificationHub Connected!'))
      .catch(err => console.error('NotificationHub Connection Error:', err));
  }

  listenForContainerUpdates(callback: (containerDto: ContainerDto) => void) {
    this.hubConnection.on('ContainerUpdated', (containerDto: ContainerDto) => {
      callback(containerDto);
    });
  }
}
