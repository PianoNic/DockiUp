import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';
import { MatSnackBar } from '@angular/material/snack-bar';

@Injectable({
  providedIn: 'root'
})
export class NotificationService {
  private notifications = new BehaviorSubject<Notification[]>([]);

  constructor(private snackBar: MatSnackBar) { }

  getNotifications(): Observable<Notification[]> {
    return this.notifications.asObservable();
  }

  addNotification(message: string, type: 'info' | 'success' | 'warning' | 'error'): void {
    // TODO: Implement method to add a new notification
  }

  markAsRead(id: string): void {
    // TODO: Implement method to mark a notification as read
  }

  markAllAsRead(): void {
    // TODO: Implement method to mark all notifications as read
  }

  clearNotifications(): void {
    // TODO: Implement method to clear all notifications
  }

  showSnackBar(message: string, action: string = 'Close', duration: number = 3000): void {
    this.snackBar.open(message, action, { duration });
  }
}