import { Example } from "./Example";

import styles from "./Example.module.css";

export type ExampleModel = {
    text: string;
    value: string;
};

const EXAMPLES: ExampleModel[] = [
    { text: "Quali batterie usa il sensore di movimento?", value: "Quali batterie usa il sensore di movimento?" },
    { text: "A quale altezza va posto il sensore di movimento?", value: "A quale altezza va posto il sensore di movimento?" },
    { text: "Quali eventi ci sono stati nella giornata di ieri?", value: "Quali eventi ci sono stati nella giornata di ieri?" }
];

interface Props {
    onExampleClicked: (value: string) => void;
}

export const ExampleList = ({ onExampleClicked }: Props) => {
    return (
        <ul className={styles.examplesNavList}>
            {EXAMPLES.map((x, i) => (
                <li key={i}>
                    <Example text={x.text} value={x.value} onClick={onExampleClicked} />
                </li>
            ))}
        </ul>
    );
};
