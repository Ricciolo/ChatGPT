export type ResponseMessage = {
    content: string;
    role: string;
};

export type ResponseContext = {
    thoughts: string | null;
    data_points: string[];
    followup_questions: string[] | null;
};

export type ResponseChoice = {
    index: number;
    message: ResponseMessage;
    session_state: any;
};

export type ChatAppResponseOrError = {
    choices?: ResponseChoice[];
    error?: string;
};

export type ChatAppResponse = {
    choices: ResponseChoice[];
};

export type ChatAppRequest = {
    messages: ResponseMessage[];
    stream?: boolean;
    session_state: any;
};
