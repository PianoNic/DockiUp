export interface GitRepo {
    url: string;
    branch: string;
    directory: string;
    credentials?: {
        username?: string;
        password?: string;
        privateKey?: string;
    };
    lastCommitHash?: string;
}