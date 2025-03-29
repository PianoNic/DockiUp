import { DockerContainer } from "./docker-container.model";
import { GitRepo } from "./git-repo.model";
import { UpdateStatus } from "./update-status.model";

export interface Container {
    id: string;
    name: string;
    description?: string;
    gitRepo: GitRepo;
    dockerContainer: DockerContainer;
    updateStatus: UpdateStatus;
    lastUpdated?: Date;
    autoUpdate: boolean;
    updateInterval?: number; // in minutes
    buildCommand?: string;
    startCommand?: string;
    stopCommand?: string;
    createdAt: Date;
    updatedAt: Date;
}