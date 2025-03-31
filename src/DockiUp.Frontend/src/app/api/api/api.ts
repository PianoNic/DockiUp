export * from './app.service';
import { AppService } from './app.service';
export * from './check.service';
import { CheckService } from './check.service';
export * from './container.service';
import { ContainerService } from './container.service';
export * from './webhook.service';
import { WebhookService } from './webhook.service';
export const APIS = [AppService, CheckService, ContainerService, WebhookService];
