import { toast } from 'react-toastify';
import type { ToastOptions, ToastPosition, ToastTransition } from 'react-toastify';

// Notification types
export const NotificationType = {
  SUCCESS: 'success',
  ERROR: 'error',
  WARNING: 'warning',
  INFO: 'info',
  MIGRATION_COMPLETE: 'migration_complete',
  MIGRATION_FAILED: 'migration_failed',
  MIGRATION_STARTED: 'migration_started',
  BATCH_COMPLETE: 'batch_complete',
  SYSTEM_ALERT: 'system_alert'
} as const;

export type NotificationType = typeof NotificationType[keyof typeof NotificationType];

// Notification priorities
export const NotificationPriority = {
  LOW: 'low',
  MEDIUM: 'medium',
  HIGH: 'high',
  CRITICAL: 'critical'
} as const;

export type NotificationPriority = typeof NotificationPriority[keyof typeof NotificationPriority];

// Notification interface
export interface Notification {
  id: string;
  type: NotificationType;
  priority: NotificationPriority;
  title: string;
  message: string;
  timestamp: Date;
  read: boolean;
  persistent?: boolean;
  soundEnabled?: boolean;
  actions?: NotificationAction[];
  migrationId?: string;
  entityType?: string;
}

// Notification action interface
export interface NotificationAction {
  label: string;
  action: () => void;
  style?: 'primary' | 'secondary' | 'danger';
}

// Sound configuration
export interface SoundConfig {
  enabled: boolean;
  volume: number;
  sounds: {
    [key in NotificationType]: string;
  };
}

// Notification service class
class NotificationService {
  private notifications: Notification[] = [];
  private soundConfig: SoundConfig;
  private listeners: ((notifications: Notification[]) => void)[] = [];

  constructor() {
    this.soundConfig = {
      enabled: true,
      volume: 0.5,
      sounds: {
        [NotificationType.SUCCESS]: '/sounds/success.mp3',
        [NotificationType.ERROR]: '/sounds/error.mp3',
        [NotificationType.WARNING]: '/sounds/warning.mp3',
        [NotificationType.INFO]: '/sounds/info.mp3',
        [NotificationType.MIGRATION_COMPLETE]: '/sounds/migration-complete.mp3',
        [NotificationType.MIGRATION_FAILED]: '/sounds/migration-failed.mp3',
        [NotificationType.MIGRATION_STARTED]: '/sounds/migration-started.mp3',
        [NotificationType.BATCH_COMPLETE]: '/sounds/batch-complete.mp3',
        [NotificationType.SYSTEM_ALERT]: '/sounds/system-alert.mp3'
      }
    };
  }

  // Show toast notification
  public showToast(
    type: NotificationType,
    title: string,
    message: string,
    options?: Partial<ToastOptions>
  ): void {
    const toastOptions: ToastOptions = {
      position: 'top-right' as ToastPosition,
      autoClose: this.getAutoCloseTime(type),
      hideProgressBar: false,
      closeOnClick: true,
      pauseOnHover: true,
      draggable: true,
      ...options
    };

    const content = `${title}\n${message}`;

    switch (type) {
      case NotificationType.SUCCESS:
      case NotificationType.MIGRATION_COMPLETE:
      case NotificationType.BATCH_COMPLETE:
        toast.success(content, toastOptions);
        break;
      case NotificationType.ERROR:
      case NotificationType.MIGRATION_FAILED:
        toast.error(content, toastOptions);
        break;
      case NotificationType.WARNING:
        toast.warning(content, toastOptions);
        break;
      case NotificationType.SYSTEM_ALERT:
        toast.warning(content, { ...toastOptions, autoClose: false });
        break;
      default:
        toast.info(content, toastOptions);
    }
  }

  // Add notification to center
  public addNotification(notification: Omit<Notification, 'id' | 'timestamp' | 'read'>): void {
    const fullNotification: Notification = {
      ...notification,
      id: this.generateId(),
      timestamp: new Date(),
      read: false
    };

    this.notifications.unshift(fullNotification);
    this.notifyListeners();

    // Show toast if not persistent
    if (!notification.persistent) {
      this.showToast(notification.type, notification.title, notification.message);
    }

    // Play sound if enabled
    if (notification.soundEnabled !== false && this.soundConfig.enabled) {
      this.playSound(notification.type);
    }
  }

  // Quick notification methods
  public success(title: string, message: string, options?: { persistent?: boolean; migrationId?: string }): void {
    this.addNotification({
      type: NotificationType.SUCCESS,
      priority: NotificationPriority.MEDIUM,
      title,
      message,
      ...options
    });
  }

  public error(title: string, message: string, options?: { persistent?: boolean; migrationId?: string }): void {
    this.addNotification({
      type: NotificationType.ERROR,
      priority: NotificationPriority.HIGH,
      title,
      message,
      ...options
    });
  }

  public warning(title: string, message: string, options?: { persistent?: boolean; migrationId?: string }): void {
    this.addNotification({
      type: NotificationType.WARNING,
      priority: NotificationPriority.MEDIUM,
      title,
      message,
      ...options
    });
  }

  public info(title: string, message: string, options?: { persistent?: boolean; migrationId?: string }): void {
    this.addNotification({
      type: NotificationType.INFO,
      priority: NotificationPriority.LOW,
      title,
      message,
      ...options
    });
  }

  // Migration-specific notifications
  public migrationStarted(migrationId: string, migrationName: string): void {
    this.addNotification({
      type: NotificationType.MIGRATION_STARTED,
      priority: NotificationPriority.MEDIUM,
      title: 'Migration Started',
      message: `Migration "${migrationName}" has started successfully`,
      migrationId,
      soundEnabled: true
    });
  }

  public migrationCompleted(migrationId: string, migrationName: string, duration: string): void {
    this.addNotification({
      type: NotificationType.MIGRATION_COMPLETE,
      priority: NotificationPriority.HIGH,
      title: 'Migration Complete',
      message: `Migration "${migrationName}" completed successfully in ${duration}`,
      migrationId,
      persistent: true,
      soundEnabled: true
    });
  }

  public migrationFailed(migrationId: string, migrationName: string, error: string): void {
    this.addNotification({
      type: NotificationType.MIGRATION_FAILED,
      priority: NotificationPriority.CRITICAL,
      title: 'Migration Failed',
      message: `Migration "${migrationName}" failed: ${error}`,
      migrationId,
      persistent: true,
      soundEnabled: true,
      actions: [
        {
          label: 'View Details',
          action: () => this.navigateToMigration(migrationId),
          style: 'primary'
        },
        {
          label: 'Retry',
          action: () => this.retryMigration(migrationId),
          style: 'secondary'
        }
      ]
    });
  }

  public batchCompleted(migrationId: string, entityType: string, completed: number, total: number): void {
    this.addNotification({
      type: NotificationType.BATCH_COMPLETE,
      priority: NotificationPriority.LOW,
      title: 'Batch Complete',
      message: `${entityType} batch completed: ${completed}/${total} items processed`,
      migrationId,
      entityType
    });
  }

  // System notifications
  public systemAlert(title: string, message: string, priority: NotificationPriority = NotificationPriority.HIGH): void {
    this.addNotification({
      type: NotificationType.SYSTEM_ALERT,
      priority,
      title,
      message,
      persistent: true,
      soundEnabled: true
    });
  }

  // Notification management
  public markAsRead(id: string): void {
    const notification = this.notifications.find(n => n.id === id);
    if (notification) {
      notification.read = true;
      this.notifyListeners();
    }
  }

  public markAllAsRead(): void {
    this.notifications.forEach(n => n.read = true);
    this.notifyListeners();
  }

  public removeNotification(id: string): void {
    this.notifications = this.notifications.filter(n => n.id !== id);
    this.notifyListeners();
  }

  public clearAllNotifications(): void {
    this.notifications = [];
    this.notifyListeners();
  }

  // Getters
  public getNotifications(): Notification[] {
    return [...this.notifications];
  }

  public getUnreadCount(): number {
    return this.notifications.filter(n => !n.read).length;
  }

  public getNotificationsByMigration(migrationId: string): Notification[] {
    return this.notifications.filter(n => n.migrationId === migrationId);
  }

  // Sound management
  public setSoundEnabled(enabled: boolean): void {
    this.soundConfig.enabled = enabled;
    localStorage.setItem('notificationSoundEnabled', enabled.toString());
  }

  public setSoundVolume(volume: number): void {
    this.soundConfig.volume = Math.max(0, Math.min(1, volume));
    localStorage.setItem('notificationSoundVolume', this.soundConfig.volume.toString());
  }

  public isSoundEnabled(): boolean {
    return this.soundConfig.enabled;
  }

  public getSoundVolume(): number {
    return this.soundConfig.volume;
  }

  // Event listeners
  public addListener(listener: (notifications: Notification[]) => void): void {
    this.listeners.push(listener);
  }

  public removeListener(listener: (notifications: Notification[]) => void): void {
    this.listeners = this.listeners.filter(l => l !== listener);
  }

  // Private methods
  private generateId(): string {
    return Date.now().toString(36) + Math.random().toString(36).substr(2);
  }

  private getAutoCloseTime(type: NotificationType): number | false {
    switch (type) {
      case NotificationType.SUCCESS:
      case NotificationType.BATCH_COMPLETE:
        return 3000;
      case NotificationType.INFO:
        return 4000;
      case NotificationType.WARNING:
        return 5000;
      case NotificationType.ERROR:
      case NotificationType.MIGRATION_FAILED:
        return 8000;
      case NotificationType.MIGRATION_COMPLETE:
        return 6000;
      case NotificationType.SYSTEM_ALERT:
        return false; // Never auto-close
      default:
        return 4000;
    }
  }

  private playSound(type: NotificationType): void {
    try {
      const audio = new Audio(this.soundConfig.sounds[type]);
      audio.volume = this.soundConfig.volume;
      audio.play().catch(error => {
        console.warn('Could not play notification sound:', error);
      });
    } catch (error) {
      console.warn('Could not create notification sound:', error);
    }
  }

  private notifyListeners(): void {
    this.listeners.forEach(listener => {
      try {
        listener([...this.notifications]);
      } catch (error) {
        console.error('Error notifying listener:', error);
      }
    });
  }

  private navigateToMigration(migrationId: string): void {
    // This would integrate with router
    window.location.href = `/migrations/${migrationId}`;
  }

  private retryMigration(migrationId: string): void {
    // This would integrate with migration API
    console.log('Retrying migration:', migrationId);
  }
}

// Export singleton instance
export const notificationService = new NotificationService();
export default notificationService; 