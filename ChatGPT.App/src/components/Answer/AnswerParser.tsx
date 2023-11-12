import { renderToStaticMarkup } from "react-dom/server";

type HtmlParsedAnswer = {
    answerHtml: string;
    citations: {title: string, path: string}[];
};

export function parseAnswerToHtml(answer: string, isStreaming: boolean, onCitationClicked: (citationFilePath: string) => void): HtmlParsedAnswer {
    const citations: {title: string, path: string}[] = [];

    // trim any whitespace from the end of the answer after removing follow-up questions
    let parsedAnswer = answer.trim();

    // Omit a citation that is still being typed during streaming
    if (isStreaming) {
        let lastIndex = parsedAnswer.length;
        for (let i = parsedAnswer.length - 1; i >= 0; i--) {
            if (parsedAnswer[i] === "]") {
                break;
            } else if (parsedAnswer[i] === "[") {
                lastIndex = i;
                break;
            }
        }
        const truncatedAnswer = parsedAnswer.substring(0, lastIndex);
        parsedAnswer = truncatedAnswer;
    }

    const parts = parsedAnswer.split(/\[([^\]]+)\]/g);

    const fragments: string[] = parts.map((part, index) => {
        if (index % 2 === 0) {
            return part;
        } else {
            const complex = part.split(/::/g);
            const item = { title: complex[0], path: complex[1] };
            let citationIndex = citations.findIndex(v => v.path == item.path);
            if (citationIndex === -1) {
                citations.push(item);
                citationIndex = citations.length;
            }

            return renderToStaticMarkup(
                <a className="supContainer" title={part} onClick={() => onCitationClicked(item.path)}>
                    <sup>{citationIndex}</sup>
                </a>
            );
        }
    });

    return {
        answerHtml: fragments.join(""),
        citations
    };
}
