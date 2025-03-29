import { UpdateStatusType } from "../enums/update-status-type.enum";

export interface UpdateStatus {
    status: UpdateStatusType;
    message?: string;
    lastCheckTime?: Date;
    lastSuccessfulUpdate?: Date;
    lastFailedUpdate?: Date;
    errorMessage?: string;
}