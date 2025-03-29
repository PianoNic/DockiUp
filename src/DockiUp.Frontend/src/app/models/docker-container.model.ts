export interface DockerContainer {
    containerId?: string;
    imageName: string;
    imageTag: string;
    ports?: {
        host: number;
        container: number;
    }[];
    volumes?: {
        host: string;
        container: string;
    }[];
    envVars?: {
        name: string;
        value: string;
    }[];
    networkMode?: string;
    restartPolicy?: string;
}